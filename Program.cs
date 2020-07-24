using ARSoft.Tools.Net.Dns;
using System;
using System.Collections.Generic;
using System.Data;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace DnsCacheDaemon
{
    /// <summary>
    /// TODO: Implement the cache via a sqlite db
    /// </summary>
    class Program
    {
        private static readonly Object _consoleLockingObject = new Object();

        public static SqliteConnector _sqliteConnector = new SqliteConnector();
        public static List<string> _customFilters = new List<string>();
        public static List<string> _customWhiteList = new List<string>();

        private static System.Timers.Timer _timer = null;

        private static int RefreshInterval = 30000;

        static void Main(string[] args)
        {
            _timer = new System.Timers.Timer(RefreshInterval);
            LoadSettings();


            _timer.Elapsed += async (sender, e) => await HandleTimer();
            _timer.Start();

            IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 53); // port 53 is the DNS port.
            using (DnsServer server = new DnsServer(endpoint, 10, 10))
            {
                server.QueryReceived += OnQueryReceived;

                server.Start();

                Console.WriteLine("DNS SERVER ACTIVE");
                Console.ReadLine();
            }
        }

        private static void LoadSettings()
        {
            Console.Write("Refreshing settings from store....");
            _customFilters.Clear();

            DataTable resultTable = _sqliteConnector.selectQuery("SELECT * FROM Settings");

            foreach (DataRow row in resultTable.Rows)
            {
                if (row["Key"].ToString() == "RefreshInterval")
                {
                    double settingsDelay = double.Parse(row["Value"].ToString());
                    if (_timer.Interval != settingsDelay)
                    {
                        _timer.Stop();
                        _timer.Interval = settingsDelay;
                        _timer.Start();
                    }
                }
            }

            LoadCustomFilters();
            Console.WriteLine("... done.");
        }

        private static Task HandleTimer() 
        {
            LoadCustomFilters();
            LogToThreadLockedConsole("::: Done refreshing settings from store.", ConsoleColor.Blue, true);
            return Task.CompletedTask;
        }

        private static void LoadCustomFilters()
        {
            _customFilters.Clear();
            _customWhiteList.Clear();

            // BLACK LIST 
            DataTable resultTable = _sqliteConnector.selectQuery("SELECT * FROM CustomFilters");

            foreach (DataRow row in resultTable.Rows)
            {
                _customFilters.Add(row[1].ToString());
            }

            // WHITE LIST 
            resultTable = _sqliteConnector.selectQuery("SELECT * FROM WhiteList");

            foreach (DataRow row in resultTable.Rows)
            {
                _customWhiteList.Add(row[1].ToString());
            }
        }

        static async Task OnQueryReceived(object sender, QueryReceivedEventArgs e)
        {
            DnsMessage query = e.Query as DnsMessage;

            LogToThreadLockedConsole(string.Format("--> DNS query recieved from {0} for {1} (Type: {2})", 
                    e.RemoteEndpoint, 
                    query.Questions[0].Name, 
                    query.Questions[0].RecordType), 
                        ConsoleColor.Green, true);

            if (query == null)
            {
                return;
            }

            DnsMessage response = query.CreateResponseInstance();
            DnsQuestion question = response.Questions[0];

            if (IsAllowed(question.Name.ToString()))
            {
                DnsClient client = new DnsClient(IPAddress.Parse("208.67.222.222"), 5000); // This is the IP for OpenDNS               
                DnsMessage upstreamResponse = await client.ResolveAsync(question.Name, question.RecordType, question.RecordClass);

                List<string> recordsRecieved = new List<string>();

                foreach (DnsRecordBase record in upstreamResponse.AnswerRecords)
                {
                    response.AnswerRecords.Add(record);

                    if (!recordsRecieved.Contains(record.Name.ToString() + "|" + record.RecordType.ToString() + "|" + record.RecordType.ToString()))
                    {
                        if (record.RecordType.ToString().ToUpper() != "OPT")
                        {
                            LogToThreadLockedConsole(string.Format("<-- Response from upstream: {0} (Type: {1})", record.Name, record.RecordType), ConsoleColor.Gray, true);
                            recordsRecieved.Add(record.Name.ToString() + "|" + record.RecordType.ToString() + "|" + record.RecordType.ToString());
                        }
                    }
                }
                /*
                foreach (DnsRecordBase record in upstreamResponse.AdditionalRecords)
                {
                    response.AnswerRecords.Add(record);

                    if (!recordsRecieved.Contains(record.Name.ToString() + "|" + record.RecordType.ToString() + "|" + record.RecordType.ToString()))
                    {
                        if (record.RecordType.ToString().ToUpper() != "OPT")
                        {
                            LogToThreadLockedConsole(string.Format("<-- Response from upstream (ADDL): {0} (Type: {1})", record.Name, record.RecordType), ConsoleColor.Gray, true);
                            recordsRecieved.Add(record.Name.ToString() + "|" + record.RecordType.ToString() + "|" + record.RecordType.ToString());
                        }
                    }
                }*/
            }

            response.ReturnCode = ReturnCode.NoError;
            e.Response = response;
            
        }

        public static void LogToThreadLockedConsole(string output, ConsoleColor foreColour, bool withTimeStamp)
        {
            lock(_consoleLockingObject)
            {
                ConsoleColor temp = Console.ForegroundColor;
                Console.ForegroundColor = foreColour;

                if (withTimeStamp)
                {
                    Console.WriteLine(DateTime.Now.ToLongTimeString() + " " + output);
                }
                else
                {
                    Console.WriteLine(output);
                }

                Console.ForegroundColor = temp;
            }
        }

        /// <summary>
        /// TODO: Load these from a sqlite DB
        /// </summary>
        private static bool IsAllowed(string recordName)
        {
            foreach(string whiteListItem in _customWhiteList)
            {
                if (recordName.Contains(whiteListItem))
                {
                    LogToThreadLockedConsole(" ^^^ WHITELISTED REQUEST ALLOWED TO PASS THROUGH FOR " + recordName.ToLower(), ConsoleColor.White, true);
                    return true; // Is Allowed - pass through.
                }
            }

            foreach (string blackListItem in _customFilters)
            {
                if (recordName.Contains(blackListItem))
                {
                    LogToThreadLockedConsole(" vvv DROPPING REQUEST FOR " + recordName.ToLower(), ConsoleColor.Red, true);
                    return false;
                }
            }

            return true; // Did not match any whitelist or blacklist, so let through.
        }
    }
}
