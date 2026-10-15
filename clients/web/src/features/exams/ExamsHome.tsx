import { useSession } from '../../auth/AuthContext';
import { StaffExamList } from '../staff/ExamListPage';
import { StudentDashboard } from '../student/DashboardPage';

export function ExamsHome() {
  return useSession().isStudent ? <StudentDashboard /> : <StaffExamList />;
}
