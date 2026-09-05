(() => {
    'use strict';
    const normalize = value => value.normalize('NFKC').toLocaleLowerCase().replace(/\s+/g, '');
    const matches = (name, query) => {
        let position = 0;
        const normalizedName = normalize(name);
        for (const character of normalize(query)) {
            position = normalizedName.indexOf(character, position);
            if (position < 0) return false;
            position++;
        }
        return true;
    };
    document.querySelectorAll('[data-parts-editor]').forEach(editor => {
        const structure = editor.querySelector('[data-structure]');
        const fields = new Map();
        editor.querySelector('.parts-structure').hidden = false;
        editor.querySelectorAll('[data-category]').forEach(field => {
            const select = field.querySelector('select');
            const search = field.querySelector('input[type=search]');
            const options = [...select.options].map(option => option.cloneNode(true));
            fields.set(field.dataset.category, { field, select });
            field.querySelector('.part-search').hidden = false;
            const filter = () => {
                const value = select.value;
                const found = options.filter(option => option.value !== '0' && matches(option.dataset.name, search.value));
                // A search alone must not change the chosen part.
                select.replaceChildren(...options.filter(option => option.value === '0' || option.value === value || found.includes(option)).map(option => option.cloneNode(true)));
                select.value = value;
                field.querySelector('[data-search-status]').textContent = found.length
                    ? `${found.length} 個符合的零件` : '沒有符合的零件；請嘗試更短的關鍵字。';
            };
            search.addEventListener('input', filter);
            select.addEventListener('change', () => { filter(); update(); });
            filter();
        });
        const option = category => {
            const select = fields.get(category).select;
            return select.disabled || select.value === '0' ? null : select.selectedOptions[0];
        };
        const show = (category, visible) => {
            const { field, select } = fields.get(category);
            field.hidden = !visible;
            select.disabled = !visible;
            field.querySelector('input').disabled = !visible;
        };
        function update() {
            const upper = structure.value === 'blade' ? ['Blade'] : structure.value === 'cx-main'
                ? ['LockChip', 'MainBlade', 'AssistBlade'] : ['LockChip', 'OverBlade', 'MetalBlade', 'AssistBlade'];
            for (const category of ['Blade', 'LockChip', 'MainBlade', 'OverBlade', 'MetalBlade', 'AssistBlade']) show(category, upper.includes(category));
            const integratedBlade = option('Blade')?.dataset.integrated === 'true';
            const integratedBit = option('Bit')?.dataset.integrated === 'true';
            show('Ratchet', !integratedBlade && !integratedBit);
            editor.querySelector('[data-ratchet-note]').hidden = !integratedBlade && !integratedBit;
            const required = [...upper, 'Bit', ...(!integratedBlade && !integratedBit ? ['Ratchet'] : [])];
            const missing = required.filter(category => !option(category));
            const conflict = integratedBlade && integratedBit;
            const valid = !conflict && missing.length === 0;
            const name = category => option(category)?.dataset.name || '';
            const commonName = name('Blade') || name('LockChip') + (name('MainBlade') || name('MetalBlade'));
            editor.querySelector('[data-common-name]').textContent = valid
                ? commonName + name('Ratchet') + name('Bit') : '選齊零件後顯示';
            const status = editor.querySelector('[data-assembly-status]');
            status.textContent = conflict ? '固鎖位置重複，請將上蓋或軸心其中之一改為非一體式。'
                : missing.length ? '尚缺：' + missing.map(category => fields.get(category).field.querySelector('label').textContent).join('、')
                : '零件已齊全，可以儲存。';
            status.classList.toggle('text-success', valid);
            status.classList.toggle('text-danger', !valid);
            return valid;
        }
        structure.addEventListener('change', update);
        editor.closest('form').addEventListener('submit', event => {
            if (!update()) {
                event.preventDefault();
                const target = [...fields.values()].find(({ select }) => !select.disabled && select.value === '0');
                (target?.select || fields.get('Bit').select).focus();
            }
        });
        update();
    });
})();
