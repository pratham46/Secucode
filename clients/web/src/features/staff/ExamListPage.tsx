import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ApiError } from '../../api/client';
import type { ExamDto } from '../../api/types';
import { useAuth, useSession } from '../../auth/AuthContext';
import { formatWindow, modeLabel } from '../../lib/format';
import { Badge, Button, DataTable, ErrorBar, Spinner, TextField, type Column } from '../../ui';
import { Dialog } from '../../ui/Dialog';
import { dateRangeDefaults, rangeQuery } from './examForm';

export function StaffExamList() {
  const { api } = useAuth();
  const session = useSession();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [range, setRange] = useState(dateRangeDefaults);
  const [toDelete, setToDelete] = useState<ExamDto | null>(null);
  const [actionError, setActionError] = useState<unknown>(null);

  const exams = useQuery({ queryKey: ['exams', range], queryFn: () => api.get<ExamDto[]>(`/api/exams?${rangeQuery(range)}`) });
  const refresh = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['exams'] }),
    queryClient.invalidateQueries({ queryKey: ['exam'] }),
  ]);

  const setActive = useMutation({
    mutationFn: (e: ExamDto) => api.patch(`/api/exams/${e.id}/active`, { isActive: !e.isActive }),
    onMutate: () => setActionError(null),
    onSuccess: refresh,
    onError: setActionError,
  });
  const remove = useMutation({
    mutationFn: (e: ExamDto) => api.del(`/api/exams/${e.id}`),
    onMutate: () => setActionError(null),
    onSuccess: refresh,
    onError: setActionError,
    onSettled: () => setToDelete(null),
  });

  const columns: Column<ExamDto>[] = [
    { header: 'Title', cell: e => <strong>{e.title}</strong> },
    { header: 'Window', cell: e => formatWindow(e.startsAt, e.endsAt) },
    { header: 'Duration', cell: e => `${e.durationMinutes} min` },
    { header: 'Mode', cell: e => <Badge tone={e.mode === 'WebAllowed' ? 'accent' : 'neutral'}>{modeLabel(e.mode)}</Badge> },
    { header: 'Status', cell: e => <Badge tone={e.isActive ? 'success' : 'neutral'}>{e.isActive ? 'Active' : 'Inactive'}</Badge> },
    { header: 'Created by', cell: e => e.createdByName },
    {
      header: 'Actions',
      cell: e => (
        <div className="row">
          {session.canViewResults(e) && <Button variant="subtle" onClick={() => navigate(`/exams/${e.id}/attempts`)}>Attempts</Button>}
          {session.canManage(e) && (
            <>
              <Button variant="subtle" onClick={() => navigate(`/exams/${e.id}/edit`)}>Edit</Button>
              <Button variant="subtle" busy={setActive.isPending && setActive.variables?.id === e.id} onClick={() => setActive.mutate(e)}>
                {e.isActive ? 'Deactivate' : 'Activate'}
              </Button>
              <Button variant="subtle" onClick={() => setToDelete(e)}>Delete</Button>
            </>
          )}
        </div>
      ),
    },
  ];

  const shownError = actionError instanceof ApiError && actionError.status === 409 && !/deactivat/i.test(actionError.detail ?? '')
    ? new ApiError(409, actionError.title, `${actionError.detail ?? ''} Deactivate the exam instead to hide it from students.`.trim())
    : actionError;

  return (
    <>
      <div className="page-header">
        <h1 className="title">Exams</h1>
        {session.canCreateExams && <Button variant="primary" onClick={() => navigate('/exams/new')}>New exam</Button>}
      </div>
      <div className="row" style={{ alignItems: 'end' }}>
        <TextField label="From" type="date" value={range.from} max={range.to} onChange={e => e.target.value && setRange(r => ({ ...r, from: e.target.value }))} />
        <TextField label="To" type="date" value={range.to} min={range.from} onChange={e => e.target.value && setRange(r => ({ ...r, to: e.target.value }))} />
        <Button onClick={() => exams.refetch()}>Refresh</Button>
      </div>
      {shownError !== null && <ErrorBar error={shownError} />}
      {exams.isPending ? <Spinner size={28} />
        : exams.isError ? <ErrorBar error={exams.error} onRetry={() => exams.refetch()} />
        : <DataTable caption="Exams" rows={exams.data} columns={columns} rowKey={e => e.id} empty="No exams in this date range." />}
      <Dialog open={toDelete !== null} title="Delete this exam?" onClose={() => setToDelete(null)}
        actions={<>
          <Button onClick={() => setToDelete(null)}>Cancel</Button>
          <Button variant="danger" busy={remove.isPending} onClick={() => toDelete && remove.mutate(toDelete)}>Delete</Button>
        </>}>
        <p>“{toDelete?.title}” will be deleted. Exams that students have attempted can't be deleted.</p>
      </Dialog>
    </>
  );
}
