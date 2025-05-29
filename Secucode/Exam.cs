using Secucode;
    public class Exam
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Date { get; set; }
        public int TimeLimit { get; set; }
        public int BranchId { get; set; }
        public string BranchName { get; set; }
        public int ClassId { get; set; }
        public string ClassName { get; set; }
        public int BatchId { get; set; }
        public string BatchName { get; set; }

        public List<Question> Questions { get; set; }

        public Exam()
        {
            Questions = new List<Question>();
        }
    }
