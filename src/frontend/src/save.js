// Individual commands commit independently. Keep the ID even when a later step fails.
// The HTTP DTO exposes photoUrl, not the Application layer's HasUploadedPhoto flag.
export function uploadedPhoto(item, id = item?.id) {
  return Boolean(id && item?.photoUrl === `/api/items/${id}/photo`);
}

export async function saveItem(api, draft) {
  draft.metadataSaved = false;
  const body = { ...draft.values, photoUrl: draft.photoMode === 'url' ? draft.values.photoUrl : (draft.baseline?.photoUrl ?? null) };
  if (draft.photoMode === 'file' && draft.file) body.photoUrl = uploadedPhoto(draft.baseline, draft.id) ? draft.baseline.photoUrl : null;
  if (draft.removePhoto && !(draft.photoMode === 'url' && draft.values.photoUrl)) body.photoUrl = null;
  if (draft.id) await api.request(`/items/${draft.id}`, { method: 'PUT', body });
  else { const saved = await api.request('/items', { method: 'POST', body }); draft.id = saved.id; }
  draft.metadataSaved = true;
  if (draft.removePhoto && uploadedPhoto(draft.baseline, draft.id) && !(draft.photoMode === 'url' && draft.values.photoUrl)) {
    await api.request(`/items/${draft.id}/photo`, { method: 'DELETE' });
    draft.baseline = { ...draft.baseline, photoUrl: null };
    draft.removePhoto = false;
  }
  if (draft.photoMode === 'file' && draft.file) {
    const form = new FormData();
    form.append('file', draft.file);
    draft.baseline = await api.request(`/items/${draft.id}/photo`, { method: 'PUT', body: form });
    draft.file = null;
  }
  return draft.id;
}
export async function saveOutfit(api, draft) {
  draft.metadataSaved = false;
  const body = { name: draft.name, description: draft.description || null };
  if (draft.id) await api.request(`/outfits/${draft.id}`, { method: 'PUT', body });
  else { const saved = await api.request('/outfits', { method: 'POST', body }); draft.id = saved.id; }
  draft.metadataSaved = true;
  const actual = await api.request(`/outfits/${draft.id}`);
  const desired = new Set(draft.selected.map(item => item.id));
  const present = new Set(actual.items.map(item => item.id));
  // Add first to avoid emptying a published/shared outfit during a replacement.
  for (const id of desired) if (!present.has(id)) await api.request(`/outfits/${draft.id}/items/${id}`, { method: 'POST' });
  for (const id of present) if (!desired.has(id)) await api.request(`/outfits/${draft.id}/items/${id}`, { method: 'DELETE' });
  return draft.id;
}
