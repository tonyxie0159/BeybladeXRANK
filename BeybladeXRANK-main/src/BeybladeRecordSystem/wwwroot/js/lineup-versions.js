(() => {
    'use strict';
    const normalize = text => text.normalize('NFKC').toLowerCase().replace(/\s+/g, '');
    const matches = (text, query) => {
        let position = 0;
        text = normalize(text);
        return [...normalize(query)].every(character => {
            position = text.indexOf(character, position);
            if (position < 0) return false;
            position++;
            return true;
        });
    };
    document.querySelectorAll('[data-version-picker]').forEach(picker => {
        const blade = picker.querySelector('[data-blade]');
        const version = picker.querySelector('[data-version]');
        const bladeSearch = picker.querySelector('[data-blade-search]');
        const versionSearch = picker.querySelector('[data-version-search]');
        const blades = [...blade.options].map(x => x.cloneNode(true));
        const versions = [...version.options].filter(x => x.dataset.bladeId).map(x => x.cloneNode(true));
        bladeSearch.hidden = blades.filter(x => x.value).length <= 7;
        const summary = () => picker.querySelector('[data-version-summary]').textContent =
            version.selectedOptions[0]?.dataset.summary || '';
        const filterVersions = reset => {
            const selected = reset ? '' : version.value;
            const bladeVersions = versions.filter(x => x.dataset.bladeId === blade.value);
            versionSearch.hidden = !blade.value || bladeVersions.length <= 4;
            const choices = bladeVersions.filter(x =>
                (x.value === selected || matches(x.textContent + ' ' + x.dataset.summary, versionSearch.value)));
            version.replaceChildren(new Option(blade.value ? '請選擇版本' : '請先選擇陀螺', ''),
                ...choices.map(x => x.cloneNode(true)));
            version.disabled = !blade.value;
            versionSearch.disabled = !blade.value;
            if (choices.some(x => x.value === selected)) version.value = selected;
            else if (reset && choices.length) version.value = choices[0].value;
            summary();
        };
        bladeSearch.addEventListener('input', () => {
            const selected = blade.value;
            blade.replaceChildren(...blades.filter(x => !x.value || x.value === selected || matches(x.textContent, bladeSearch.value)).map(x => x.cloneNode(true)));
            blade.value = selected;
        });
        blade.addEventListener('change', () => { versionSearch.value = ''; filterVersions(true); });
        versionSearch.addEventListener('input', () => filterVersions(false));
        version.addEventListener('change', summary);
        filterVersions(true);
    });
})();
