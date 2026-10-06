namespace Secucode
{
    public class Exam
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public DateTime ExamDate { get; set; }
        public string Date => ExamDate.ToShortDateString();
        public int TimeLimit { get; set; }
    }
}
