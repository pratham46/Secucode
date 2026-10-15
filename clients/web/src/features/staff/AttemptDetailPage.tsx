import { useQuery } from '@tanstack/react-query';
import { useNavigate, useParams } from 'react-router-dom';
import { isLanguage, type AttemptDetailDto } from '../../api/types';
import { useAuth } from '../../auth/AuthContext';
import { eventLabel, formatDateTime, statusLabel } from '../../lib/format';
import { Badge, Button, Card, DataTable, ErrorBar, Spinner } from '../../ui';
import { CodeEditor } from '../../ui/CodeEditor';
import { EvidenceView } from './EvidenceView';

export function AttemptDetailPage() {
  const { id } = useParams();
  const { api } = useAuth();
  const navigate = useNavigate();
  const detail = useQuery({ queryKey: ['attempt', id], queryFn: () => api.get<AttemptDetailDto>(`/api/attempts/${id}`) });

  if (detail.isPending) return <Spinner size={28} />;
  if (detail.isError) return <ErrorBar error={detail.error} onRetry={() => detail.refetch()} />;
  const { attempt, studentName, events, evidence } = detail.data;
  const language = isLanguage(attempt.language) ? attempt.language : 'Python';

  return (
    <>
      <div className="page-header">
        <h1 className="title">{studentName}</h1>
        <Badge tone={attempt.status === 'InProgress' ? 'accent' : 'neutral'}>{statusLabel(attempt.status)}</Badge>
        <Button onClick={() => navigate(`/exams/${attempt.examId}/attempts`)}>Back to attempts</Button>
      </div>
      <Card className="stack">
        <p className="secondary">
          Attempt {attempt.attemptNumber} · {attempt.client} · started {formatDateTime(attempt.startedAt)}
          {attempt.submittedAt && ` · submitted ${formatDateTime(attempt.submittedAt)}`} · {attempt.strikes} strikes
        </p>
        {attempt.submitReason && <p><strong>Submit reason:</strong> {attempt.submitReason}</p>}
        <h2 className="subtitle">Question</h2>
        <p className="question-text">{attempt.questionText}</p>
      </Card>
      <Card className="stack">
        <h2 className="subtitle">Code ({attempt.language ?? 'not saved yet'})</h2>
        <div className="code-box"><CodeEditor value={attempt.code} language={language} readOnly label="Submitted code" /></div>
      </Card>
      <Card className="stack">
        <h2 className="subtitle">Proctoring events</h2>
        <DataTable caption="Proctoring events" rows={events} rowKey={e => e.id} empty="No events."
          columns={[
            { header: 'Time', cell: e => formatDateTime(e.occurredAt) },
            { header: 'Event', cell: e => eventLabel(e.type) },
            { header: 'Detail', cell: e => e.detail ?? '' },
            { header: 'Evidence', cell: e => (e.evidenceId ? <a href={`#evidence-${e.evidenceId}`}>View</a> : '') },
          ]} />
      </Card>
      <Card className="stack">
        <h2 className="subtitle">Evidence</h2>
        {evidence.length === 0 ? <p className="secondary">No evidence uploaded.</p>
          : <div className="evidence-grid">{evidence.map(ev => <EvidenceView key={ev.id} evidence={ev} />)}</div>}
      </Card>
    </>
  );
}
