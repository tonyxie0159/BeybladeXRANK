(() => {
    const normalizePath = value => (value || "/").replace(/\/$/, "") || "/";
    const currentPath = normalizePath(window.location.pathname);

    document.querySelectorAll(".site-header a[href], .mobile-dock a[href]").forEach(link => {
        const linkPath = normalizePath(new URL(link.href, window.location.origin).pathname);
        const sectionPath = linkPath.replace(/\/(Create|Index)$/, "");
        const isHome = linkPath === "/" && currentPath === "/";
        const isSection = linkPath !== "/" && currentPath.startsWith(sectionPath);
        if (isHome || isSection) link.setAttribute("aria-current", "page");
    });

    document.querySelectorAll(".alert").forEach(alert => {
        alert.setAttribute("role", alert.classList.contains("alert-danger") ? "alert" : "status");
    });

    document.querySelectorAll("table.table").forEach(table => {
        if (table.parentElement?.classList.contains("table-responsive")) return;
        const wrapper = document.createElement("div");
        wrapper.className = "table-responsive";
        table.before(wrapper);
        wrapper.append(table);
    });

    document.querySelectorAll("form[method='post']").forEach(form => {
        form.addEventListener("submit", event => {
            if (event.defaultPrevented || !form.checkValidity() || form.dataset.submitting === "true") {
                if (form.dataset.submitting === "true") event.preventDefault();
                return;
            }

            form.dataset.submitting = "true";
            const submitter = event.submitter;
            if (submitter instanceof HTMLButtonElement) {
                window.requestAnimationFrame(() => {
                    submitter.disabled = true;
                    submitter.setAttribute("aria-busy", "true");
                    submitter.textContent = "處理中…";
                });
            }
        });
    });

    document.querySelectorAll("#primaryNavigation .nav-link").forEach(link => {
        link.addEventListener("click", () => {
            const navigation = document.getElementById("primaryNavigation");
            if (!navigation || !navigation.classList.contains("show") || !window.bootstrap) return;
            window.bootstrap.Collapse.getOrCreateInstance(navigation).hide();
        });
    });

    const navigation = document.getElementById("primaryNavigation");
    const navigationToggle = document.querySelector("[aria-controls='primaryNavigation']");
    if (navigation && navigationToggle) {
        const updateNavigationToggleLabel = expanded => {
            const label = expanded
                ? navigationToggle.dataset.expandedLabel
                : navigationToggle.dataset.collapsedLabel;
            if (label) navigationToggle.setAttribute("aria-label", label);
        };

        navigation.addEventListener("show.bs.collapse", () => updateNavigationToggleLabel(true));
        navigation.addEventListener("hide.bs.collapse", () => updateNavigationToggleLabel(false));
        updateNavigationToggleLabel(navigationToggle.getAttribute("aria-expanded") === "true");
    }
})();
