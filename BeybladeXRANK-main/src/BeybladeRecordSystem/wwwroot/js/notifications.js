(() => {
    const bell = document.getElementById("notificationBell");
    if (!bell) return;

    const badge = document.getElementById("notificationBadge");
    const countLabel = document.getElementById("notificationCountLabel");
    const preview = document.getElementById("notificationPreview");
    const toastRegion = document.getElementById("notificationToastRegion");
    const token = document.querySelector("#notificationActionForm input[name='__RequestVerificationToken']")?.value;
    let pageHasUnsavedInput = false;
    let wasHidden = document.hidden;
    const currentEntityId = () => {
        const queryId = Number(new URLSearchParams(window.location.search).get("id"));
        if (queryId) return queryId;
        const lastSegment = window.location.pathname.split("/").filter(Boolean).at(-1);
        return Number(lastSegment) || 0;
    };

    const escapeHtml = value => String(value || "").replace(/[&<>'"]/g, character => ({
        "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;"
    })[character]);

    const renderCount = count => {
        const unread = Number(count) || 0;
        badge.hidden = unread === 0;
        countLabel.textContent = unread === 0 ? "沒有未讀訊息" : `${unread} 則未讀訊息`;
        bell.setAttribute("aria-label", unread === 0 ? "通知，沒有未讀訊息" : `通知，${unread} 則未讀訊息`);
    };

    const renderPreview = items => {
        if (!items?.length) {
            preview.innerHTML = '<p class="text-muted mb-0 p-3">目前沒有未讀通知。</p>';
            return;
        }
        preview.innerHTML = items.map(item => `
            <a class="notification-preview-item" href="/Notifications?handler=Open&id=${encodeURIComponent(item.id)}">
                <strong>${escapeHtml(item.title)}</strong>
                <span>${escapeHtml(item.message)}</span>
            </a>`).join("");
    };

    const syncUnread = async () => {
        try {
            const response = await fetch("/Notifications?handler=Unread", { cache: "no-store", headers: { Accept: "application/json" } });
            if (!response.ok) return;
            const data = await response.json();
            renderCount(data.count);
            renderPreview(data.items);
        } catch {
            countLabel.textContent = "通知同步暫時中斷";
        }
    };

    const syncCurrentFlow = () => {
        if (pageHasUnsavedInput) return;
        const path = window.location.pathname;
        const isLiveFlow = path === "/Battles/Invitations" ||
            path.startsWith("/Battles/Setup/") || path.startsWith("/Battles/Battle/") ||
            path.startsWith("/Battles/Reorder/") || path.startsWith("/Tournaments/Match/") ||
            path.startsWith("/Tournaments/Details/") || path === "/Tournaments" || path === "/Tournaments/Index";
        if (isLiveFlow) window.location.reload();
    };

    const acceptNotification = async id => {
        const body = new URLSearchParams({ id: String(id), __RequestVerificationToken: token || "" });
        const response = await fetch("/Notifications?handler=Accept", {
            method: "POST",
            body,
            headers: { Accept: "application/json", "Content-Type": "application/x-www-form-urlencoded" }
        });
        const data = await response.json();
        if (!data.succeeded) throw new Error(data.error || "邀請目前無法接受。");
        window.location.assign(data.targetUrl || "/Notifications");
    };

    const showToast = item => {
        if (!toastRegion || !item?.id) return;
        const hasAction = item.actionType && item.actionType !== "None";
        const element = document.createElement("section");
        element.className = "notification-toast";
        element.innerHTML = `
            <div><strong>${escapeHtml(item.title)}</strong><p>${escapeHtml(item.message)}</p></div>
            <div class="notification-toast-actions">
                ${hasAction ? `<button type="button" class="btn btn-success btn-sm" data-accept>接受</button>` : ""}
                <a class="btn btn-outline-secondary btn-sm" href="${escapeHtml(item.targetUrl || "/Notifications")}">前往處理</a>
            </div>`;
        element.querySelector("[data-accept]")?.addEventListener("click", async event => {
            event.currentTarget.disabled = true;
            try { await acceptNotification(item.id); }
            catch (error) {
                event.currentTarget.disabled = false;
                element.querySelector("p").textContent = error.message;
                element.classList.add("is-error");
            }
        });
        toastRegion.prepend(element);
        window.setTimeout(() => element.remove(), 6000);
    };

    const handleRealtimeEvent = message => {
        if (!message) return;
        if (message.eventType === "notification") showToast(message.payload);
        if (message.eventType === "battle-state" && window.location.pathname.startsWith("/Battles/")) {
            const currentId = currentEntityId();
            if (currentId === Number(message.payload?.battleId) && message.payload?.targetUrl) {
                const current = `${window.location.pathname}${window.location.search}`;
                if (current !== message.payload.targetUrl) window.location.assign(message.payload.targetUrl);
                else window.location.reload();
            }
        }
        if (message.eventType === "battle-state" && window.location.pathname.startsWith("/Tournaments/Match/")) {
            const currentId = currentEntityId();
            if (currentId === Number(message.payload?.tournamentMatchId) && message.payload?.targetUrl) {
                const current = `${window.location.pathname}${window.location.search}`;
                if (current !== message.payload.targetUrl) window.location.assign(message.payload.targetUrl);
                else window.location.reload();
            }
        }
        if (message.eventType === "tournament-state" && window.location.pathname.startsWith("/Tournaments/Details/")) {
            const currentId = currentEntityId();
            if (currentId === Number(message.payload?.tournamentId) && message.payload?.targetUrl) {
                const current = `${window.location.pathname}${window.location.search}`;
                if (current !== message.payload.targetUrl) window.location.assign(message.payload.targetUrl);
                else window.location.reload();
            }
        }
        if (message.eventType === "tournament-state" && (window.location.pathname === "/Tournaments" || window.location.pathname === "/Tournaments/Index")) {
            window.location.reload();
        }
        if (message.eventType === "match-state") {
            const inMatch = window.location.pathname.startsWith("/Tournaments/Match/") && currentEntityId() === Number(message.payload?.matchId);
            const inBattle = window.location.pathname.startsWith("/Battles/Battle/") && currentEntityId() === Number(message.payload?.battleId);
            if ((inMatch || inBattle) && message.payload?.targetUrl) {
                const current = `${window.location.pathname}${window.location.search}`;
                if (current !== message.payload.targetUrl) window.location.assign(message.payload.targetUrl);
                else window.location.reload();
            }
        }
        window.dispatchEvent(new CustomEvent("beyblade:realtime", { detail: message }));
        syncUnread();
    };

    const connect = async () => {
        if (!window.signalR) return;
        const connection = new window.signalR.HubConnectionBuilder()
            .withUrl("/hubs/realtime")
            .withAutomaticReconnect([0, 1000, 3000, 10000, 30000])
            .build();
        connection.on("RealtimeEvent", handleRealtimeEvent);
        connection.onreconnected(() => {
            syncUnread();
            syncCurrentFlow();
        });
        try { await connection.start(); }
        catch { window.setTimeout(connect, 10000); }
    };

    document.addEventListener("input", event => {
        if (event.target instanceof HTMLInputElement || event.target instanceof HTMLSelectElement || event.target instanceof HTMLTextAreaElement)
            pageHasUnsavedInput = true;
    });
    document.addEventListener("change", event => {
        if (event.target instanceof HTMLInputElement || event.target instanceof HTMLSelectElement || event.target instanceof HTMLTextAreaElement)
            pageHasUnsavedInput = true;
    });
    document.addEventListener("submit", () => pageHasUnsavedInput = false);
    document.addEventListener("visibilitychange", () => {
        if (document.hidden) {
            wasHidden = true;
            return;
        }
        syncUnread();
        if (wasHidden) {
            wasHidden = false;
            syncCurrentFlow();
        }
    });
    window.addEventListener("focus", syncUnread);
    bell.addEventListener("show.bs.dropdown", syncUnread);
    syncUnread();
    connect();
    window.setInterval(syncUnread, 60000);
})();
