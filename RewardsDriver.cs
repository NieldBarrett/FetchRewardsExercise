using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace FetchRewardsExercise
{
    class RewardsDriver
    {
        private static string url = "http://localhost:8000/";
        private static HttpListener? listener=null;
        //Use RewardTotals to store transactions in memory 
        private static RewardTotals rewardTotals = new RewardTotals() {
            PayerValues = new Dictionary<string, int>(),
            Transactions = new List<Transactions>()
        };
        static void Main(string[] args)
        {
            if (!HttpListener.IsSupported)
            {
                Console.WriteLine("HttpListener is not supported.");
                return;
            }

            //Run Server
            using (listener = new HttpListener())
            {
                listener.Prefixes.Add(url);
                listener.Start();
                listener.BeginGetContext(RequestListener, null);
                Console.WriteLine("Listening. Press Enter to stop.");
                Console.ReadLine();
                listener.Stop();
            }
        }

        /// <summary>
        /// Listen for HTTP Requests
        /// </summary>
        /// <param name="ar"></param>
        private static void RequestListener(IAsyncResult ar)
        {
            HttpListenerContext context = listener.EndGetContext(ar);
            listener.BeginGetContext(RequestListener, null);

            //Get and Process the Request
            var requestBody = new StreamReader(context.Request.InputStream).ReadToEnd();
            JObject requestJson =  JObject.Parse(requestBody);
            HandleRequest(requestJson, context);
        }

        /// <summary>
        /// Sends a response back to the caller
        /// </summary>
        /// <param name="context">The HTTPListener Context object</param>
        /// <param name="success">If the request was successful</param>
        /// <param name="responseStr">The response body to send to the client</param>
        /// <param name="contentType">The response context (plain text or JSON for this app)</param>
        private static void SendResponse(HttpListenerContext context, bool success, string responseStr, string contentType)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(responseStr);
            // and send it
            HttpListenerResponse response = context.Response;
            response.ContentType = contentType;
            response.ContentLength64 = buffer.Length;
            response.StatusCode = success ? 200:400;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }


        /// <summary>
        /// Act on the HTTP request 
        /// </summary>
        /// <param name="requestJson">The Json Object from the request</param>
        /// <param name="context">The HTTPListener Context object</param>
        private static void HandleRequest(JObject requestJson, HttpListenerContext context)
        {
            bool success = true;
            string response="";
            string contentType = "";

            //If the request has 3 parameters assume it's adding points, if it has 2 parameters assume it's spending points, if there are no parameters assume it's requesting payer totals 
            //Request to add points
            if (requestJson.Count == 3) 
            {
                lock (rewardTotals)
                {
                    try
                    {
                        Transactions transaction = new Transactions()
                        {
                            Payer = requestJson.GetValue("payer").ToString().ToUpper(), //Force to Upper to avoid issues with mix case names
                            PointValue = int.Parse(requestJson.GetValue("points").ToString()),
                            TimeStamp = DateTime.Parse(requestJson.GetValue("timestamp").ToString())
                        };

                        response = AddPoints(transaction);
                    }
                    catch (Exception ex) 
                    {
                        success = false;
                        response = ex.Message;
                    }

                    contentType = "text/plain";
                }
            }
            //Request to spend points
            else if(requestJson.Count == 1)
            {
                int cost = int.Parse(requestJson.GetValue("points").ToString());

                //Verify that we have enough points before trying to spend them
                if(rewardTotals.total >= cost)
                {
                    lock (rewardTotals)
                    {
                        try
                        {
                            response=SpendPoints(cost);
                            rewardTotals.total -= cost;
                            contentType = "application/json";
                        }
                        catch (Exception ex)
                        {
                            success = false;
                            response = ex.Message;
                            contentType = "text/plain";
                        }
                    }
                }
                else
                {
                    response = "This transaction would cost more than the accounts total points.";
                    success = true;
                    contentType = "text/plain";
                }
            }
            //Return Point Values for each payer
            else if (requestJson.Count == 0)
            {
                try
                {
                    response = ReturnBalances();
                    contentType = "application/json";
                }
                catch(Exception ex)
                {
                    success = false;
                    response = ex.Message;
                    contentType = "text/plain";
                }
            }

            SendResponse(context,success, response,contentType);
        }

        /// <summary>
        /// Add a transaction to the rewardTotals object
        /// </summary>
        /// <param name="transaction"></param>
        private static string AddPoints(Transactions transaction)
        {
            if (rewardTotals.PayerValues.ContainsKey(transaction.Payer))
            {
                rewardTotals.PayerValues[transaction.Payer] += transaction.PointValue;
            }
            else
            {
                rewardTotals.PayerValues[transaction.Payer] = transaction.PointValue;
            }
            rewardTotals.total+=transaction.PointValue;
            rewardTotals.Transactions.Add(transaction);

            return String.Format("{0} points added to {1} payer", transaction.PointValue, transaction.Payer);
        }

        /// <summary>
        /// Spend points, should always spend the points with the oldest timestamp first 
        /// </summary>
        /// <param name="cost"></param>
        private static string SpendPoints(int cost)
        {
            //Sort the transactions in chronological order
            rewardTotals.SortTransactions();

            Dictionary<string, int> spentPoints = new Dictionary<string, int>();
            Transactions transactionToSpend;

            while(cost > 0)
            {
                transactionToSpend = rewardTotals.Transactions.First();

                if (!spentPoints.ContainsKey(transactionToSpend.Payer))
                {
                    spentPoints.Add(transactionToSpend.Payer, 0);
                }

                //If the remaining cost is greater than the transaction remove the entire transaction
                if(cost >= transactionToSpend.PointValue)
                {
                    rewardTotals.Transactions.RemoveAt(0);
                    spentPoints[transactionToSpend.Payer] -= transactionToSpend.PointValue;
                    cost -= transactionToSpend.PointValue;
                }
                else
                {
                    spentPoints[transactionToSpend.Payer] -= cost;
                    transactionToSpend.PointValue -= cost;
                    cost = 0;
                    rewardTotals.Transactions[0]=transactionToSpend;
                }
            }
                        
            List<PayerResponseValues> returnObj = new List<PayerResponseValues>();

            //Remove the points needed for this transaction from the total for each payer
            foreach (string key in spentPoints.Keys)
            {
                rewardTotals.PayerValues[key] += spentPoints[key];
                returnObj.Add( new PayerResponseValues() { Payer=key, PointValue=spentPoints[key] });
            }
            return JsonConvert.SerializeObject(returnObj);

        }

        /// <summary>
        /// Returns a serialzed string of all the payers and their current point totals
        /// </summary>
        private static string ReturnBalances()
        {
            List<PayerResponseValues> payerTotals = new List<PayerResponseValues>();
            foreach(string payer in rewardTotals.PayerValues.Keys)
            {
                payerTotals.Add(new PayerResponseValues() { Payer = payer, PointValue = rewardTotals.PayerValues[payer] });
            }

            return JsonConvert.SerializeObject(payerTotals);
        }
    }
}
