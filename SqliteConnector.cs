using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DnsCacheDaemon
{
    class SqliteConnector
    {
        private SQLiteConnection sqlite;

        public SqliteConnector()
        {
            sqlite = new SQLiteConnection("Data Source=DCD.db;New=False;");
        }

        public DataTable selectQuery(string query)
        {
            SQLiteDataAdapter ad;
            DataTable dt = new DataTable();

            try
            {
                SQLiteCommand cmd;
                sqlite.Open();  //Initiate connection to the db
                cmd = sqlite.CreateCommand();
                cmd.CommandText = query;  //set the passed query
                ad = new SQLiteDataAdapter(cmd);
                ad.Fill(dt); //fill the datasource
            }
            catch (SQLiteException)
            {
                //Add your exception code here.
            }

            sqlite.Close();
            return dt;
        }
    }
}