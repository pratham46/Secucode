namespace Secucode.Contracts;

public enum ClientKind { Desktop, Web }

public enum ExamMode { DesktopOnly, WebAllowed }

public enum AttemptStatus { InProgress, Submitted, AutoSubmitted, Voided }

// InScope: anywhere inside the holder's assignment scope. OwnOnly: only on exams the holder created.
public enum Reach { InScope, OwnOnly }

[Flags]
public enum ScopeLevels { None = 0, Institution = 1, Department = 2, Year = 4, Batch = 8 }

public enum Capability
{
    ManageSettings, ManageMasters, ManageRoles, AssignRoles, ManageUsers,
    CreateExams, ManageExams,
    ResetAttempts, ReopenAttempts, ExtendTime, EditQuestionsAfterAttempts,
    ViewResults, ViewEvidence, ViewAudit,
}

public enum AttemptEventType { FaceMissing, MultipleFaces, FaceMismatch, FocusLost, Minimized, TabHidden, FullscreenExited, AudioUnavailable }

public enum EvidenceKind { ReferenceFace, Webcam, Screenshot, Audio }
