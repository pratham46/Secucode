namespace Secucode
{
    // Teacher-side view of an exam (with its questions and target branch/class/batch).
    public class Test
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public DateTime? ExamDate { get; set; }
        public string Date => ExamDate?.ToShortDateString() ?? "";
        public int TimeLimit { get; set; }
        public string CreatedBy { get; set; } = "";
        public bool IsActive { get; set; }
        public List<string> Questions { get; set; } = new();

        public int BranchId { get; set; }
        public string BranchName { get; set; } = "";
        public int ClassId { get; set; }
        public string ClassName { get; set; } = "";
        public int BatchId { get; set; }
        public string BatchName { get; set; } = "";
    }
}
