using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetPGMQ
{
    public class PGQEvent
    {
        public long Ev_id { get; private set; }
        public DateTime Ev_time { get; private set; }
        public long Ev_txid { get; private set; }
        public int Ev_retry { get; private set; }
        public String Ev_type { get; private set; }
        public String Ev_data { get; private set; }
        public String Ev_extra1 { get; private set; }
        public String Ev_extra2 { get; private set; }
        public String Ev_extra3 { get; private set; }
        public String Ev_extra4 { get; private set; }
        public PGQEvent()
        {

        }
        public override String ToString()
        {
            return "PGQEvent [id=" + Ev_id + ", time=" + Ev_time + ", txid=" + Ev_txid + ", retry=" + Ev_retry
                    + ", type=" + Ev_type + ", data=" + Ev_data + ", extra1=" + Ev_extra1 + ", extra2=" + Ev_extra2
                    + ", extra3=" + Ev_extra3 + ", extra4=" + Ev_extra4 + "]";
        }
    }
}
