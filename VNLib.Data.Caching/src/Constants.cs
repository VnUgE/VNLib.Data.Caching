using System;

using VNLib.Net.Messaging.FBM;

namespace VNLib.Data.Caching
{
    public static class Constants
    {
        /// <summary>
        /// Contains constants the define actions
        /// </summary>
        public static class Actions
        {
            public const string Get= "g";
            public const string AddOrUpdate = "u";
            public const string Delete = "d";
            public const string Dequeue = "dq";
        }
        /// <summary>
        /// Containts constants for operation response codes
        /// </summary>
        public static class ResponseCodes
        {
            public const string Okay = "ok";
            public const string Error = "err";
            public const string NotFound = "nf";
        }

        public const HeaderCommand ObjectId = (HeaderCommand)0xAA;
        public const HeaderCommand NewObjectId = (HeaderCommand)0xAB;
    }
}
