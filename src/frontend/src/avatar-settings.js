import { avatar } from './components.js';
import { ApiError } from './api.js';
import { busy, showError } from './ui.js';

export function cropGeometry(width, height, zoom = 1, horizontal = 50, vertical = 50) {
  const size = Math.max(1, Math.floor(Math.min(width, height) / zoom));
  return { x: Math.round((width - size) * horizontal / 100), y: Math.round((height - size) * vertical / 100), size };
}
export function avatarSettings(user) {
  return `<section class="account-panel"><h2>Аватар</h2><div id="avatar-current">${avatar(user)}</div><form id="avatar-form"><label>Изображение JPEG или PNG, до 5 МБ<input type="file" name="file" accept="image/jpeg,image/png"></label><div id="avatar-crop" hidden><canvas width="256" height="256" aria-label="Предпросмотр квадратной обрезки"></canvas><label>Масштаб<input name="zoom" type="range" min="1" max="5" step="0.01" value="1"></label><label>По горизонтали<input name="horizontal" type="range" min="0" max="100" value="50"></label><label>По вертикали<input name="vertical" type="range" min="0" max="100" value="50"></label><button class="btn" type="submit">Сохранить аватар</button><button class="btn outline" type="button" id="avatar-cancel">Отмена</button></div><p class="error" id="avatar-error" role="alert" tabindex="-1" hidden></p><p id="avatar-status" role="status"></p><button class="btn outline danger" type="button" id="avatar-delete" ${user.avatarUrl ? '' : 'hidden'}>Удалить аватар</button></form></section>`;
}
export function bindAvatarSettings(ctx, { canSave, setSaving, setDirty, updateIdentity }) {
  const form = document.querySelector('#avatar-form');
  const preview = form.querySelector('#avatar-crop');
  const error = form.querySelector('#avatar-error');
  const fileInput = form.elements.file;
  let image, file, url, generation = 0, disposed = false, pending = false;
  const active = () => !disposed && ctx.isCurrent() && form.isConnected;
  const release = () => { if (url) URL.revokeObjectURL(url); url = null; };
  function reset() {
    generation++; release(); image = null; file = null; fileInput.value = ''; preview.hidden = true; setDirty(false);
  }
  function paint() {
    if (!image || !active()) return;
    const crop = cropGeometry(image.naturalWidth, image.naturalHeight, Number(form.elements.zoom.value), Number(form.elements.horizontal.value), Number(form.elements.vertical.value));
    const canvas = form.querySelector('canvas'), drawing = canvas.getContext('2d');
    drawing.clearRect(0, 0, 256, 256); drawing.drawImage(image, crop.x, crop.y, crop.size, crop.size, 0, 0, 256, 256);
    return crop;
  }
  fileInput.onchange = async () => {
    if (pending) return;
    const selected = fileInput.files?.[0];
    reset(); error.hidden = true;
    if (!selected) return;
    const mine = generation;
    try {
      if (!['image/jpeg', 'image/png'].includes(selected.type)) throw new ApiError(415);
      if (selected.size > 5 * 1024 * 1024) throw new ApiError(413);
      const candidate = new Image(); url = URL.createObjectURL(selected); candidate.src = url;
      await candidate.decode();
      if (!active() || mine !== generation) return;
      if (!candidate.naturalWidth || candidate.naturalWidth > 8192 || candidate.naturalHeight > 8192 || candidate.naturalWidth * candidate.naturalHeight > 25000000) throw new ApiError(400);
      image = candidate; file = selected;
      form.elements.zoom.value = '1'; form.elements.horizontal.value = form.elements.vertical.value = '50';
      preview.hidden = false; setDirty(true); paint();
    } catch (e) { if (active() && mine === generation) { reset(); showError(error, e); } }
  };
  for (const name of ['zoom', 'horizontal', 'vertical']) form.elements[name].oninput = paint;
  form.querySelector('#avatar-cancel').onclick = () => { if (!pending) reset(); };
  async function save(remove) {
    if (pending || !canSave() || !active() || !remove && !file) return;
    const crop = remove ? null : paint();
    const data = new FormData();
    if (!remove) { data.append('file', file); data.append('cropX', String(crop.x)); data.append('cropY', String(crop.y)); data.append('cropSize', String(crop.size)); }
    pending = true; setSaving(true); busy(form, true); error.hidden = true;
    try {
      const user = remove ? await ctx.auth.deleteAvatar() : await ctx.auth.uploadAvatar(data);
      if (!active()) return;
      reset(); updateIdentity(); document.querySelector('#avatar-current').innerHTML = avatar(user);
      form.querySelector('#avatar-delete').hidden = !user.avatarUrl;
      form.querySelector('#avatar-status').textContent = remove ? 'Аватар удалён.' : 'Аватар сохранён.';
    } catch (e) { if (active()) showError(error, e); }
    finally { pending = false; busy(form, false); setSaving(false); }
  }
  form.onsubmit = event => { event.preventDefault(); save(false); };
  form.querySelector('#avatar-delete').onclick = () => save(true);
  return () => { disposed = true; generation++; release(); };
}
