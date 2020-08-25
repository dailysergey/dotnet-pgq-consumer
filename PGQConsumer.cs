using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetPGMQ
{
    public class PgMQProvider : IHostedService
    {
        private int REGISTER_SUCCESS = 1;
        private int BACKOFF_MILLIS = 100;
        private int EVENT_RETRY_SECONDS = 10;

        readonly CancellationTokenSource tokenSource = new CancellationTokenSource();
        readonly ILogger _logger;
        public String _queueName;
        public String _consumerName;
        public IPGQEventHandler _eventHandler;
        readonly PostgresOptions _pgOptions;
        readonly object syncRoot = new object();
        Timer pollingQueue;
        public PgMQProvider(IOptions<PostgresOptions> pgOptions, ILogger<PgMQProvider> logger)
        {
            _logger = logger;
            _pgOptions = pgOptions.Value;
            this._queueName = _pgOptions.QuequeName;
            this._consumerName = _pgOptions.ConsumerName;
        }


        public void MQCallback(object state)
        {
            if (!Monitor.TryEnter(syncRoot))
            {
                _logger.LogDebug($"[MQCallback.TryEnter]: Process is busy!");
                return;
            }
            try
            {
                _logger.LogInformation($"[MQCallback]: Process has started - {DateTime.Now}");
                Run();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[MQCallback.Exception]: {ex.ToString()}");
            }
            finally
            {
                Monitor.Exit(syncRoot);
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                pollingQueue = new Timer(MQCallback, tokenSource.Token, TimeSpan.FromMinutes(0), TimeSpan.FromSeconds(EVENT_RETRY_SECONDS));
                return Task.CompletedTask;
            }
            catch (Exception e)
            {
                _logger.LogError($"[StartAsync.Exception]:{e.ToString()}");
                throw;
            }
        }

        /// <summary>
        /// Polls PGQ for events and processes them when found.
        /// </summary>
        public void Run()
        {
            CreateQueueIfNeeded();
            RegisterIfNeeded();

            long? batchId = GetNextBatchId();
            if (batchId.HasValue)
            {
                ProcessEvents(batchId.Value, GetNextBatch(batchId.Value));
                FinishBatch(batchId.Value);
            }
        }

        /// <summary>
        /// Main proccess of handling message from db
        /// </summary>
        /// <param name="batchId"></param>
        /// <param name="events"></param>
        private void ProcessEvents(long batchId, List<PGQEvent> events)
        {
            foreach (PGQEvent ev in events)
            {
                try
                {
                    _logger.LogInformation($"[PgMQProvider.ProcessEvents]: handling message with ID: {ev.Ev_id}");
                    _eventHandler.Handle(ev);
                }
                catch (Exception e)
                {
                    _logger.LogError($"[PgMQProvider.ProcessEvents.Exception]: {e.ToString()}");
                    RetryEvent(batchId, ev.Ev_id, EVENT_RETRY_SECONDS);
                }
            }
        }

        private void RetryEvent(long batchId, long eventId, int eventRetryTime)
        {
            int result;
            using (var cnt = new Npgsql.NpgsqlConnection(_pgOptions.ConnectionString))
            {
                cnt.Open();
                using (var cmd = new Npgsql.NpgsqlCommand())
                {
                    cmd.Connection = cnt;
                    cmd.Parameters.AddWithValue("@batchId", batchId);
                    cmd.Parameters.AddWithValue("@eventId", eventId);
                    cmd.Parameters.AddWithValue("@eventRetryTime", eventRetryTime);
                    cmd.CommandText = "select * from pgq.event_retry(@batchId, @eventId, @eventRetryTime);";
                    result = (int)cmd.ExecuteScalar();
                }
            }
            if (result != 1)
            {
                _logger.LogError($"Error scheduling event {eventId} of batch {batchId} for retry");
            }
        }

        /// <summary>
        /// Get the ID of the next batch to be processed.
        /// </summary>
        /// <returns>The next batch ID to process, or null if there are no more events available.</returns>
        private long? GetNextBatchId()
        {
            long? batchID;
            using (var cnt = new Npgsql.NpgsqlConnection(_pgOptions.ConnectionString))
            {
                cnt.Open();
                using (var cmd = new Npgsql.NpgsqlCommand())
                {
                    cmd.Connection = cnt;
                    cmd.Parameters.AddWithValue("@queueName", _queueName);
                    cmd.Parameters.AddWithValue("@consumeName", _consumerName);
                    cmd.CommandText = "select * from pgq.next_batch(@queueName,@consumeName);";
                    if (long.TryParse(cmd.ExecuteScalar().ToString(), out long result))
                        batchID = result;
                    else
                        batchID = null;
                }
            }
            return batchID;
        }

        /// <summary>
        /// Get the next batch of events.
        /// </summary>
        /// <param name="batchId">Batch of events to retrieve.</param>
        /// <returns>List of Event objects that were in that batch.</returns>
        private List<PGQEvent> GetNextBatch(long batchId)
        {
            _logger.LogInformation($"[GetNextBatch.PgMQProvider]: Getting next events batch for ID {batchId}");

            List<PGQEvent> events;
            using (var cnt = new Npgsql.NpgsqlConnection(_pgOptions.ConnectionString))
            {
                cnt.Open();
                using (var cmd = new Npgsql.NpgsqlCommand())
                {
                    cmd.Connection = cnt;
                    cmd.Parameters.AddWithValue("@batchId", batchId);
                    cmd.CommandText = "select * from pgq.get_batch_events(@batchId);";

                    events = DbHandler.MapToList<PGQEvent>(cmd.ExecuteReader());
                }
            }
            return events;
        }

        /// <summary>
        /// The next batch ID to process, or null if there are no more events available.
        /// </summary>
        private void RegisterIfNeeded()
        {
            bool registerSuccess = Register();
            if (registerSuccess)
            {
                _logger.LogInformation("[RegisterIfNeeded]: PGQ consumer registered successfully for the first time.");
            }
        }

        /// <summary>
        /// Check queue existence
        /// </summary>
        private void CreateQueueIfNeeded()
        {
            bool createdSuccess = CreateQueue();
            if (createdSuccess)
            {
                _logger.LogInformation("[CreateQueueIfNeeded]: Queue is created.");
            }
        }

        /// <summary>
        /// Finish the given batch.
        /// </summary>
        /// <param name="batchId">The batch to finish.</param>
        /// <returns></returns>
        private int FinishBatch(long batchId)
        {
            _logger.LogDebug($"Finishing batch ID {batchId}");
            int batchID;
            using (var cnt = new Npgsql.NpgsqlConnection(_pgOptions.ConnectionString))
            {
                cnt.Open();
                using (var cmd = new Npgsql.NpgsqlCommand())
                {
                    cmd.Connection = cnt;
                    cmd.Parameters.AddWithValue("@batchId", batchId);
                    cmd.CommandText = "select * from pgq.finish_batch(@batchId);";
                    batchID = (int)cmd.ExecuteScalar();
                }
            }
            return batchID;
        }

        /// <summary>
        /// creating queue
        /// </summary>
        /// <returns></returns>
        private bool CreateQueue()
        {
            try
            {
                int registerID;
                using (var cnt = new Npgsql.NpgsqlConnection(_pgOptions.ConnectionString))
                {
                    cnt.Open();
                    using (var cmd = new Npgsql.NpgsqlCommand())
                    {
                        cmd.Connection = cnt;
                        cmd.Parameters.AddWithValue("@queueName", _queueName);
                        cmd.CommandText = "select * from pgq.create_queue(@queueName);";
                        registerID = (int)cmd.ExecuteScalar();
                    }
                }
                return (REGISTER_SUCCESS == registerID);
            }
            catch (Exception e)
            {
                _logger.LogError($"[PgMQProvider.CreateQueue.Exception]: {e.ToString()}");
                return false;
            }
        }

        /// <summary>
        /// The result of the registration. 1 means success, 0 means already registered.
        /// </summary>
        /// <returns></returns>
        private bool Register()
        {
            try
            {
                int registerID;
                using (var cnt = new Npgsql.NpgsqlConnection(_pgOptions.ConnectionString))
                {
                    cnt.Open();
                    using (var cmd = new Npgsql.NpgsqlCommand())
                    {
                        cmd.Connection = cnt;
                        cmd.Parameters.AddWithValue("@queueName", _queueName);
                        cmd.Parameters.AddWithValue("@consumeName", _consumerName);
                        cmd.CommandText = "select * from pgq.register_consumer(@queueName,@consumeName);";
                        registerID = (int)cmd.ExecuteScalar();
                    }
                }
                return (REGISTER_SUCCESS == registerID);
            }
            catch (Exception e)
            {
                _logger.LogError($"[PgMQProvider.Register.Exception]: {e.ToString()}");
                return false;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            tokenSource.Cancel();
            pollingQueue.Change(Timeout.Infinite, Timeout.Infinite);
            return Task.CompletedTask;
        }
    }
}

