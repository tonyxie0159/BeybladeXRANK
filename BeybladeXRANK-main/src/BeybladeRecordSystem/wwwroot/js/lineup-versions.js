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
    const controllers = [];

    const validateLineup = () => {
        const conflicts = new Map(controllers.map(controller => [controller, []]));
        const bladeUses = new Map();
        const partUses = new Map();

        controllers.forEach(controller => {
            if (controller.blade.value) {
                const uses = bladeUses.get(controller.blade.value) || [];
                uses.push(controller);
                bladeUses.set(controller.blade.value, uses);
            }

            let parts = [];
            try {
                parts = JSON.parse(controller.version.selectedOptions[0]?.dataset.parts || '[]');
            } catch {
                parts = [];
            }
            parts.forEach(part => {
                const use = partUses.get(String(part.id)) || { name: part.name, controllers: [] };
                use.controllers.push(controller);
                partUses.set(String(part.id), use);
            });
        });

        bladeUses.forEach(uses => {
            if (uses.length < 2) return;
            uses.forEach(current => {
                const others = uses.filter(x => x !== current).map(x => x.position).join('、');
                conflicts.get(current).push(`與位置 ${others} 使用同一顆陀螺`);
            });
        });
        partUses.forEach(use => {
            if (use.controllers.length < 2) return;
            use.controllers.forEach(current => {
                const others = use.controllers.filter(x => x !== current).map(x => x.position).join('、');
                conflicts.get(current).push(`與位置 ${others} 重複零件「${use.name}」`);
            });
        });

        let hasConflict = false;
        controllers.forEach(controller => {
            const messages = [...new Set(conflicts.get(controller))];
            const conflict = controller.picker.querySelector('[data-lineup-conflict]');
            conflict.textContent = messages.join('；');
            conflict.hidden = messages.length === 0;
            controller.picker.classList.toggle('lineup-picker-conflict', messages.length > 0);
            controller.blade.setAttribute('aria-invalid', messages.length > 0 ? 'true' : 'false');
            controller.version.setAttribute('aria-invalid', messages.length > 0 ? 'true' : 'false');
            hasConflict ||= messages.length > 0;
        });
        document.querySelectorAll('[data-lineup-submit]').forEach(button => {
            button.disabled = hasConflict;
            button.title = hasConflict ? '請先排除陣容中的重複陀螺或零件' : '';
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
                x.value === selected || matches(`${x.textContent} ${x.dataset.summary}`, versionSearch.value));
            version.replaceChildren(new Option(blade.value ? '請選擇版本' : '請先選擇陀螺', ''),
                ...choices.map(x => x.cloneNode(true)));
            version.disabled = !blade.value;
            versionSearch.disabled = !blade.value;
            if (choices.some(x => x.value === selected)) version.value = selected;
            else if (reset && choices.length) version.value = choices[0].value;
            summary();
        };
        const applySelection = (bladeId, configurationId) => {
            bladeSearch.value = '';
            blade.replaceChildren(...blades.map(x => x.cloneNode(true)));
            blade.value = bladeId;
            versionSearch.value = '';
            filterVersions(true);
            if ([...version.options].some(x => x.value === configurationId)) version.value = configurationId;
            summary();
        };

        const controller = {
            picker,
            blade,
            version,
            position: picker.dataset.position,
            applySelection
        };
        controllers.push(controller);
        bladeSearch.addEventListener('input', () => {
            const selected = blade.value;
            blade.replaceChildren(...blades
                .filter(x => !x.value || x.value === selected || matches(x.textContent, bladeSearch.value))
                .map(x => x.cloneNode(true)));
            blade.value = selected;
        });
        blade.addEventListener('change', () => {
            versionSearch.value = '';
            filterVersions(true);
            validateLineup();
        });
        versionSearch.addEventListener('input', () => filterVersions(false));
        version.addEventListener('change', () => {
            summary();
            validateLineup();
        });
        filterVersions(false);
    });

    document.querySelectorAll('[data-apply-recent-lineup]').forEach(button => {
        button.addEventListener('click', () => {
            const form = button.closest('form');
            const formControllers = controllers.filter(x => form?.contains(x.picker));
            formControllers.forEach(controller => controller.applySelection(
                controller.picker.dataset.recentBladeId,
                controller.picker.dataset.recentConfigurationId));
            validateLineup();
            const status = form?.querySelector('[data-recent-lineup-status]');
            if (status) status.textContent = '已帶入最近陣容，送出前仍可調整。';
        });
    });

    validateLineup();
})();
