namespace FetchRewardsExercise
{
    /// <summary>
    /// Class for individual transaction data
    /// </summary>
    public class Transactions
    {
        public string Payer { get; set; }
        public int PointValue { get; set; }
        public DateTime TimeStamp { get; set; }
    }

    /// <summary>
    /// Class for an individual rewards payer 
    /// </summary>
    public class RewardTotals
    {
        public int total { get; set; }
        public Dictionary<string, int> PayerValues { get; set; }
        public List<Transactions>? Transactions { get; set; }

        public void SortTransactions()
        {
            this.Transactions.Sort((x, y) => x.TimeStamp.CompareTo(y.TimeStamp));
        }
    }

    /// <summary>
    /// Class used to keep track of payer/points spent on a transaction
    /// </summary>
    public class PayerResponseValues
    {
        public string Payer { get; set; }
        public int PointValue { get; set; }
    }
}
