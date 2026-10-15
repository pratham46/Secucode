import { useQuery } from '@tanstack/react-query';
import { useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import type { StudentExamDto } from '../../api/types';
import { useAuth } from '../../auth/AuthContext';
import { formatDateTime, formatWindow } from '../../lib/format';
import { closeMedia } from '../../proctoring/media';
import { Badge, Button, Card, ErrorBar, Spinner } from '../../ui';
import { examAction } from './studentExams';

export function StudentDashboard() {
  const { api } = useAuth();
  const navigate = useNavigate();
  const exams = useQuery({
    queryKey: ['student-exams'],
    queryFn: () => api.get<StudentExamDto[]>('/api/exams'),
    refetchInterval: 30_000,
  });

  useEffect(() => closeMedia(), []); // back from the start page: turn the camera off

  return (
    <>
      <div className="page-header">
        <h1 className="title">Exams</h1>
        <Button onClick={() => exams.refetch()}>Refresh</Button>
      </div>
      {exams.isPending ? <Spinner size={28} />
        : exams.isError ? <ErrorBar error={exams.error} onRetry={() => exams.refetch()} />
        : exams.data.length === 0 ? <p className="empty">You have no exams open now or coming up.</p>
        : (
          <div className="exam-cards">
            {exams.data.map(e => {
              const action = examAction(e);
              return (
                <Card as="article" key={e.id} className="exam-card">
                  <h2 className="subtitle">{e.title}</h2>
                  <p className="secondary">{formatWindow(e.startsAt, e.endsAt)} · {e.durationMinutes} min</p>
                  <div className="row">
                    {action.kind === 'desktop' && <Badge>Desktop app only</Badge>}
                    {action.kind === 'upcoming' && <span className="secondary">Opens {formatDateTime(new Date(action.opensAt).toISOString())}</span>}
                    {action.kind === 'start' && <Button variant="primary" onClick={() => navigate(`/exams/${e.id}/start`)}>Start</Button>}
                    {action.kind === 'resume' && (
                      <Button variant="primary" onClick={() => navigate(`/exams/${e.id}/start?resume=${action.attemptId}`)}>Resume</Button>
                    )}
                  </div>
                </Card>
              );
            })}
          </div>
        )}
    </>
  );
}
