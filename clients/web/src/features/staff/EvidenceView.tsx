import { useQuery } from '@tanstack/react-query';
import { useEffect, useMemo } from 'react';
import { ApiError } from '../../api/client';
import type { EvidenceDto } from '../../api/types';
import { useAuth } from '../../auth/AuthContext';
import { formatDateTime } from '../../lib/format';
import { Spinner } from '../../ui';

// Evidence needs the bearer token, so it's fetched as a blob and shown through an object URL.
export function EvidenceView({ evidence }: { evidence: EvidenceDto }) {
  const { api } = useAuth();
  const file = useQuery({ queryKey: ['evidence', evidence.id], queryFn: () => api.get<Blob>(`/api/evidence/${evidence.id}`), staleTime: Infinity });
  const url = useMemo(() => (file.data ? URL.createObjectURL(file.data) : null), [file.data]);
  useEffect(() => () => { if (url) URL.revokeObjectURL(url); }, [url]);

  const caption = `${evidence.kind} · ${formatDateTime(evidence.createdAt)}`;
  let body;
  if (file.isPending) body = <Spinner />;
  else if (file.isError)
    body = <p className="secondary">{file.error instanceof ApiError && file.error.status === 404 ? "You can't view this file." : 'Could not load this file.'}</p>;
  else if (evidence.contentType.startsWith('audio/')) body = <audio controls src={url!} aria-label={caption} />;
  else body = <img src={url!} alt={caption} />;

  return (
    <figure className="evidence" id={`evidence-${evidence.id}`}>
      {body}
      <figcaption className="caption secondary">{caption}</figcaption>
    </figure>
  );
}
