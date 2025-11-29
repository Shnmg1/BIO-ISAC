using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace api.DataAccess
{
    public class Database
    {
        public string host { get; set; }
        public string database { get; set; }
        public string username { get; set; }
        public string password { get; set; }
        public int port { get; set; }
        public string connectionString {get; set;}

        public Database(){
            // HOSTED DATABASE
            host = "e11wl4mksauxgu1w.cbetxkdyhwsb.us-east-1.rds.amazonaws.com";
            database = "h06zjyj5jaxgvfe1";
            username = "dbix45lycgaqo0zi";
            password = "nwtbc6r0x3i2vfn1";
            port = 3306;
            connectionString = $"Server={host};Database={database};User Id={username};Password={password};Port={port};";
        }
    }
}