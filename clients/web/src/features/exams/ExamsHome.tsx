import { useSession } from '../../auth/AuthContext';
import { StaffExamList } from '../staff/ExamListPage';

// Task 9 adds the student dashboard branch.
export function ExamsHome() {
  return useSession().isStudent ? <h1 className="title">Exams</h1> : <StaffExamList />;
}
