import { createElement, useEffect, useRef, useState } from '@cove/runtime/react';
import { useQuery } from '@cove/runtime/react-query';
import { extensionFetch } from '@cove/runtime/api';
import { watchCards, loadSizes } from './cards.mjs';
import { watchFilesizeFilters } from './filters.mjs';

function useSizes(kind, ids) {
  return useQuery({
    queryKey: ['filesizes', kind, ids],
    queryFn: ({ signal }) => loadSizes(kind, ids, extensionFetch, signal),
    enabled: ids.length > 0,
    staleTime: 30000, refetchInterval: 30000, retry: false,
  });
}
export function Filesizes() {
  const [ids, setIds] = useState({ performer: [], studio: [], video: [] });
  const watcher = useRef(null);
  const performer = useSizes('performer', ids.performer);
  const studio = useSizes('studio', ids.studio);
  const video = useSizes('video', ids.video);
  useEffect(() => {
    watcher.current = watchCards(document, setIds);
    const filters = watchFilesizeFilters(document);
    return () => { filters.stop(); watcher.current.stop(); watcher.current = null; };
  }, []);
  useEffect(() => { watcher.current?.update('performer', performer.isError ? [] : performer.data ?? []); }, [performer.data, performer.isError]);
  useEffect(() => { watcher.current?.update('studio', studio.isError ? [] : studio.data ?? []); }, [studio.data, studio.isError]);
  useEffect(() => { watcher.current?.update('video', video.isError ? [] : video.data ?? []); }, [video.data, video.isError]);
  return null;
}
export function FilesizeCardIdentity({ performer, studio, video }) {
  const kind = performer ? 'performer' : studio ? 'studio' : video ? 'video' : null;
  const entity = performer ?? studio ?? video;
  if (!kind || !Number.isSafeInteger(entity.id) || entity.id <= 0) return null;
  return createElement('span', { hidden: true, 'data-cove-filesize-kind': kind, 'data-cove-filesize-id': entity.id });
}
export default { components: { Filesizes, FilesizeCardIdentity } };
