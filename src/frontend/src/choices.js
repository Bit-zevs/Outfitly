const colors = { 'Белый':'#ffffff','Молочный':'#f5eedc','Бежевый':'#d6bfa0','Коричневый':'#80583e','Чёрный':'#29241e','Серый':'#93918d','Синий':'#365c91','Голубой':'#9fcbe3','Зелёный':'#628364','Хаки':'#83825a','Красный':'#bc4943','Бордовый':'#803e50','Розовый':'#dfa4b5','Фиолетовый':'#9276ad','Жёлтый':'#eac65c','Оранжевый':'#dd9155','Разноцветный':'conic-gradient(#bc4943,#eac65c,#628364,#365c91,#bc4943)' };
export function enhanceChoices(root) {
  root.querySelectorAll('select').forEach(select => {
    if (select.dataset.enhanced) return;
    select.dataset.enhanced = 'true';
    const label = select.labels?.[0];
    const labelCopy = label?.cloneNode(true); labelCopy?.querySelectorAll('select,input,button').forEach(node => node.remove());
    const name = labelCopy?.textContent.trim() || select.getAttribute('aria-label') || select.name || 'Выберите значение';
    const wrapper = document.createElement('div'); wrapper.className = 'choice';
    const trigger = document.createElement('button'); trigger.type = 'button'; trigger.className = 'choice-trigger';
    const list = document.createElement('div'); list.className = 'choice-list'; list.hidden = true; list.setAttribute('role', 'listbox');
    const id = 'choice-' + (select.id || select.name || 'field') + '-' + document.querySelectorAll('.choice').length;
    list.id = id; list.setAttribute('aria-label', name);
    trigger.setAttribute('aria-haspopup', 'listbox'); trigger.setAttribute('aria-expanded', 'false'); trigger.setAttribute('aria-controls', id);
    if (select.dataset.filterFocus) trigger.dataset.filterFocus = select.dataset.filterFocus;
    select.after(wrapper); wrapper.append(trigger, list); select.hidden = true;
    if (!select.id) select.id = id + '-native';
    trigger.id = id + '-trigger';
    label?.addEventListener('click', event => {
      if (event.target === label) { event.preventDefault(); trigger.focus(); }
    });
    const swatches = select.name === 'color'; if (swatches) wrapper.classList.add('color-choice');
    const decorate = (target, text) => {
      target.textContent = '';
      const color = colors[text.replace(/^value:/, '')];
      if (color) { const dot = document.createElement('span'); dot.className = 'color-swatch'; dot.style.background = color; dot.setAttribute('aria-hidden','true'); target.append(dot); }
      const span = document.createElement('span'); span.textContent = text; target.append(span);
    };
    function sync() {
      decorate(trigger, select.selectedOptions[0]?.textContent || 'Выберите значение');
      trigger.disabled = select.disabled;
      for (const attribute of ['aria-describedby', 'aria-invalid', 'aria-errormessage']) {
        if (select.hasAttribute(attribute)) trigger.setAttribute(attribute, select.getAttribute(attribute));
        else trigger.removeAttribute(attribute);
      }
      trigger.setAttribute('aria-label', name + (select.required ? ' (обязательное поле)' : '') + ': ' + trigger.textContent);
      if (select.disabled) close();
    }
    function close(focus = false) { list.hidden = true; trigger.setAttribute('aria-expanded','false'); if (focus) trigger.focus(); }
    function open() {
      list.replaceChildren();
      [...select.options].forEach(option => {
        const button = document.createElement('button'); button.type = 'button'; button.tabIndex = -1; button.setAttribute('role','option'); button.setAttribute('aria-selected',String(option.selected)); button.disabled = option.disabled; decorate(button,option.textContent);
        button.onclick = event => { event.preventDefault(); select.value = option.value; sync(); close(true); select.dispatchEvent(new select.ownerDocument.defaultView.Event('input',{bubbles:true})); select.dispatchEvent(new select.ownerDocument.defaultView.Event('change',{bubbles:true})); };
        list.append(button);
      });
      list.hidden = false; trigger.setAttribute('aria-expanded','true');
      (list.querySelector('[aria-selected="true"]') || list.querySelector('button:not(:disabled)'))?.focus();
    }
    trigger.onclick = event => { event.preventDefault(); list.hidden ? open() : close(); };
    wrapper.onkeydown = event => {
      if (event.key === 'Escape' && !list.hidden) { event.stopPropagation(); event.preventDefault(); close(true); return; }
      if (event.key === 'Tab' && !list.hidden) { close(true); return; }
      if (['ArrowDown','ArrowUp','Home','End'].includes(event.key)) {
        event.preventDefault(); if (list.hidden) { open(); return; }
        const buttons = [...list.querySelectorAll('button:not(:disabled)')], index = buttons.indexOf(document.activeElement);
        buttons[event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1 : (index + (event.key === 'ArrowDown' ? 1 : -1) + buttons.length) % buttons.length]?.focus();
      } else if (event.key.length === 1 && !list.hidden && !event.ctrlKey && !event.metaKey) {
        [...list.querySelectorAll('button')].find(button => button.textContent.toLocaleLowerCase().startsWith(event.key.toLocaleLowerCase()))?.focus();
      }
    };
    select.addEventListener('change',sync);
    new select.ownerDocument.defaultView.MutationObserver(sync).observe(select,{attributes:true,childList:true,subtree:true});
    wrapper.addEventListener('focusout',event => { if (!wrapper.contains(event.relatedTarget)) close(); });
    sync();
  });
}
export function installChoices(root) {
  enhanceChoices(root);
  const observer = new (root.defaultView || root.ownerDocument.defaultView).MutationObserver(() => enhanceChoices(root));
  observer.observe(root,{childList:true,subtree:true});
  return () => observer.disconnect();
}
