using System;
using System.Collections.Generic;

namespace Secucode
{
    public class Test
    {
        // Basic test properties
        public int Id { get; set; }
        public string Name { get; set; }
        public string Date { get; set; }
        public int TimeLimit { get; set; }
        public string CreatedBy { get; set; }
        public bool IsActive { get; set; }
        public List<string> Questions { get; set; }

        // Property to hold the name and ID for Branch
        public int BranchId { get; set; }
        public string BranchName { get; set; }

        // Property to hold the name and ID for Class
        public int ClassId { get; set; }
        public string ClassName { get; set; }

        // Property to hold the name and ID for Batch
        public int BatchId { get; set; }
        public string BatchName { get; set; }

        // Constructor
        public Test()
        {
            Questions = new List<string>();
        }

        // Nested classes for Branch, Class, and Batch if you want to manage them separately
        public class Branch
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        public class Class
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        public class Batch
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }
    }
}
