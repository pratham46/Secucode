namespace Secucode.Api.Data.Entities;

// Masters share a shape so one set of endpoints can serve all three (Task 5).
public abstract class MasterEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class Branch : MasterEntity { }      // a department, e.g. Computer Engineering

public class SchoolClass : MasterEntity { } // a year, e.g. TE

public class Batch : MasterEntity { }       // a group within a year, e.g. A1
