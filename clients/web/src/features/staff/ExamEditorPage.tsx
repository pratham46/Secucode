import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useBlocker, useNavigate, useParams } from 'react-router-dom';
import type { ExamDto, ExamMode, MasterDto } from '../../api/types';
import { useAuth } from '../../auth/AuthContext';
import { Button, Card, ErrorBar, Select, Spinner, TextArea, TextField, Toggle } from '../../ui';
import { Dialog } from '../../ui/Dialog';
import { emptyForm, formFromExam, move, newQuestion, toRequest, validate, type ExamForm, type FormErrors } from './examForm';

export function ExamEditorPage() {
  const { id } = useParams();
  const editing = id !== undefined;
  const { api } = useAuth();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const masters = (path: string) => ({ queryKey: [path], queryFn: () => api.get<MasterDto[]>(`/api/${path}`), staleTime: 300_000 });
  const branches = useQuery(masters('branches'));
  const classes = useQuery(masters('classes'));
  const batches = useQuery(masters('batches'));
  const exam = useQuery({ queryKey: ['exam', id], queryFn: () => api.get<ExamDto>(`/api/exams/${id}`), enabled: editing });

  const [form, setForm] = useState<ExamForm | null>(() => (editing ? null : emptyForm()));
  const [errors, setErrors] = useState<FormErrors>({});
  const [saveError, setSaveError] = useState<unknown>(null);
  const [saving, setSaving] = useState(false);
  const dirty = useRef(false);

  useEffect(() => {
    if (exam.data && form === null) setForm(formFromExam(exam.data));
  }, [exam.data, form]);

  // Leaving with unsaved changes: in-app navigation asks; closing the tab gets the browser's own prompt.
  const blocker = useBlocker(({ currentLocation, nextLocation }) => dirty.current && currentLocation.pathname !== nextLocation.pathname);
  useEffect(() => {
    const onUnload = (e: BeforeUnloadEvent) => { if (dirty.current) e.preventDefault(); };
    addEventListener('beforeunload', onUnload);
    return () => removeEventListener('beforeunload', onUnload);
  }, []);

  const update = (patch: Partial<ExamForm>) => {
    dirty.current = true;
    setForm(f => (f ? { ...f, ...patch } : f));
  };

  async function save(e: FormEvent) {
    e.preventDefault();
    if (!form) return;
    const found = validate(form);
    setErrors(found);
    if (Object.keys(found).length > 0) return;
    setSaving(true);
    setSaveError(null);
    try {
      const body = toRequest(form);
      if (editing) await api.put(`/api/exams/${id}`, body);
      else await api.post('/api/exams', body);
      dirty.current = false;
      await queryClient.invalidateQueries({ queryKey: ['exams'] });
      await queryClient.invalidateQueries({ queryKey: ['exam', id] });
      navigate('/exams');
    } catch (x) {
      setSaveError(x);
      setSaving(false);
    }
  }

  const loadError = exam.error ?? branches.error ?? classes.error ?? batches.error;
  if (loadError)
    return <ErrorBar error={loadError} onRetry={() => { void exam.refetch(); void branches.refetch(); void classes.refetch(); void batches.refetch(); }} />;
  if (!form || !branches.data || !classes.data || !batches.data) return <Spinner size={28} />;

  const options = (list: MasterDto[]) => list.map(m => <option key={m.id} value={m.id}>{m.name}</option>);
  const setQuestions = (questions: ExamForm['questions']) => update({ questions });

  return (
    <form className="stack" onSubmit={save} noValidate>
      <div className="page-header">
        <h1 className="title">{editing ? 'Edit exam' : 'New exam'}</h1>
        <Button onClick={() => navigate('/exams')}>Cancel</Button>
        <Button type="submit" variant="primary" busy={saving}>Save exam</Button>
      </div>
      {saveError !== null && <ErrorBar error={saveError} />}

      <Card className="stack">
        <TextField label="Title" value={form.title} error={errors.title} onChange={e => update({ title: e.target.value })} />
        <div className="grid-2">
          <TextField label="Starts" type="datetime-local" value={form.startsAt} onChange={e => update({ startsAt: e.target.value })} hint="Your local time." />
          <TextField label="Ends" type="datetime-local" value={form.endsAt} error={errors.endsAt} onChange={e => update({ endsAt: e.target.value })} />
          <TextField label="Duration (minutes)" type="number" min={1} inputMode="numeric" value={form.durationMinutes} error={errors.durationMinutes}
            onChange={e => update({ durationMinutes: e.target.value })} />
          <Select label="Mode" value={form.mode} onChange={e => update({ mode: e.target.value as ExamMode })}>
            <option value="DesktopOnly">Desktop only (strict proctoring)</option>
            <option value="WebAllowed">Web allowed</option>
          </Select>
        </div>
      </Card>

      <Card className="stack">
        <h2 className="subtitle">Who takes it</h2>
        <div className="grid-3">
          <Select label="Department" value={form.branchId} error={errors.branchId} onChange={e => update({ branchId: e.target.value })}>
            <option value="">Choose…</option>{options(branches.data)}
          </Select>
          <Select label="Year (optional)" value={form.classId} onChange={e => update({ classId: e.target.value })}>
            <option value="">Whole department</option>{options(classes.data)}
          </Select>
          <Select label="Batch (optional)" value={form.batchId} onChange={e => update({ batchId: e.target.value })}>
            <option value="">Whole year</option>{options(batches.data)}
          </Select>
        </div>
      </Card>

      <Card className="stack">
        <h2 className="subtitle">Settings</h2>
        <Toggle label="Active (students can see it)" checked={form.isActive} onChange={v => update({ isActive: v })} />
        <Toggle label="Allow editing questions after attempts" checked={form.allowQuestionEditAfterAttempts} onChange={v => update({ allowQuestionEditAfterAttempts: v })} />
        <Toggle label="Allow resetting attempts" checked={form.allowAttemptReset} onChange={v => update({ allowAttemptReset: v })} />
        <Toggle label="Allow reopening attempts" checked={form.allowAttemptReopen} onChange={v => update({ allowAttemptReopen: v })} />
        <Toggle label="Allow time extensions" checked={form.allowTimeExtension} onChange={v => update({ allowTimeExtension: v })} />
      </Card>

      <Card className="stack">
        <h2 className="subtitle">Questions</h2>
        <p className="secondary">Each student gets one of these at random.</p>
        {errors.questions && <p role="alert" style={{ color: 'var(--danger)' }}>{errors.questions}</p>}
        {form.questions.map((q, i) => (
          <div key={q.key} className="question-row">
            <TextArea label={`Question ${i + 1}`} value={q.text} rows={3}
              onChange={e => setQuestions(form.questions.map(x => (x.key === q.key ? { ...x, text: e.target.value } : x)))} />
            <div className="row">
              <Button variant="subtle" aria-label={`Move question ${i + 1} up`} disabled={i === 0} onClick={() => setQuestions(move(form.questions, i, -1))}>↑</Button>
              <Button variant="subtle" aria-label={`Move question ${i + 1} down`} disabled={i === form.questions.length - 1} onClick={() => setQuestions(move(form.questions, i, 1))}>↓</Button>
              <Button variant="subtle" aria-label={`Remove question ${i + 1}`} disabled={form.questions.length === 1}
                onClick={() => setQuestions(form.questions.filter(x => x.key !== q.key))}>Remove</Button>
            </div>
          </div>
        ))}
        <div><Button onClick={() => setQuestions([...form.questions, newQuestion()])}>Add question</Button></div>
      </Card>

      <Dialog open={blocker.state === 'blocked'} title="Leave without saving?" onClose={() => blocker.reset?.()}
        actions={<>
          <Button onClick={() => blocker.reset?.()}>Stay</Button>
          <Button variant="danger" onClick={() => blocker.proceed?.()}>Leave</Button>
        </>}>
        <p>Your changes to this exam will be lost.</p>
      </Dialog>
    </form>
  );
}
