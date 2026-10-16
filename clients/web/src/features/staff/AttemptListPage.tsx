import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from 'react-router-dom';
import type { AttemptSummaryDto, ExamDto } from '../../api/types';
import { useAuth } from '../../auth/AuthContext';
import { formatDateTime, statusLabel } from '../../lib/format';
import { Badge, Button, DataTable, ErrorBar, Spinner, type Column } from '../../ui';

export function AttemptListPage() {
  const { id } = useParams();
  const { api } = useAuth();
  const navigate = useNavigate();
  const exam = useQuery({ queryKey: ['exam', id], queryFn: () => api.get<ExamDto>(`/api/exams/${id}`) });
  const attempts = useQuery({ queryKey: ['attempts', id], queryFn: () => api.get<AttemptSummaryDto[]>(`/api/exams/${id}/attempts`) });

  const columns: Column<AttemptSummaryDto>[] = [
    { header: 'Student', cell: a => <Link to={`/attempts/${a.id}`}>{a.studentName}</Link> },
    { header: 'Attempt', cell: a => a.attemptNumber },
    { header: 'Status', cell: a => <Badge tone={a.status === 'InProgress' ? 'accent' : a.status === 'Voided' ? 'warning' : 'success'}>{statusLabel(a.status)}</Badge> },
    { header: 'App', cell: a => a.client },
    { header: 'Started', cell: a => formatDateTime(a.startedAt) },
    { header: 'Submitted', cell: a => (a.submittedAt ? formatDateTime(a.submittedAt) : '—') },
    { header: 'Strikes', cell: a => (a.strikes > 0 ? <Badge tone="danger">{a.strikes}</Badge> : 0) },
  ];

  return (
    <>
      <div className="page-header">
        <h1 className="title">{exam.data ? `Attempts · ${exam.data.title}` : 'Attempts'}</h1>
        <Button onClick={() => attempts.refetch()}>Refresh</Button>
        <Button onClick={() => navigate('/exams')}>Back to exams</Button>
      </div>
      {attempts.isPending ? <Spinner size={28} />
        : attempts.isError ? <ErrorBar error={attempts.error} onRetry={() => attempts.refetch()} />
        : <DataTable caption="Attempts" rows={attempts.data} columns={columns} rowKey={a => a.id} empty="Nobody has started this exam yet." />}
    </>
  );
}
