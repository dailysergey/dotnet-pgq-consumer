using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetPGMQ
{
    public class PostgresOptions
    {
        public string ConnectionString { get; set; }
        public string ConsumerName { get; set; }
        public string QuequeName { get; set; }
    }
}
