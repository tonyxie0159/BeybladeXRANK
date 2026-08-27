(() => {
    document.querySelectorAll("[data-player-search]").forEach(input => {
        const hidden = document.getElementById(input.dataset.target);
        const results = document.getElementById(input.dataset.results);
        const tournamentId = input.dataset.tournamentId;
        if (!hidden || !results) return;
        let timer;
        let controller;

        const close = () => { results.replaceChildren(); results.hidden = true; };
        input.addEventListener("input", () => {
            hidden.value = "";
            window.clearTimeout(timer);
            controller?.abort();
            const query = input.value.trim();
            if (!query) { close(); return; }
            timer = window.setTimeout(async () => {
                controller = new AbortController();
                const url = new URL("/Players/Search", window.location.origin);
                url.searchParams.set("q", query);
                if (tournamentId) url.searchParams.set("tournamentId", tournamentId);
                try {
                    const response = await fetch(url, { cache: "no-store", signal: controller.signal, headers: { Accept: "application/json" } });
                    if (!response.ok) return;
                    const players = await response.json();
                    results.replaceChildren(...players.map(player => {
                        const button = document.createElement("button");
                        button.type = "button";
                        button.className = "player-search-option";
                        button.textContent = player.displayName;
                        button.addEventListener("click", () => {
                            input.value = player.displayName;
                            hidden.value = player.userId;
                            close();
                        });
                        return button;
                    }));
                    results.hidden = players.length === 0;
                } catch (error) { if (error.name !== "AbortError") close(); }
            }, 250);
        });
        input.form?.addEventListener("submit", event => {
            if (!hidden.value) { event.preventDefault(); input.setCustomValidity("請從搜尋結果選擇玩家。"); input.reportValidity(); }
            else input.setCustomValidity("");
        });
        document.addEventListener("click", event => { if (event.target !== input && !results.contains(event.target)) close(); });
    });
})();
