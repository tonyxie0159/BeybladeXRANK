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

    const restoreSubmissionState = form => {
        delete form.dataset.submitting;
        form.querySelectorAll("button[data-submit-lock='true']").forEach(button => {
            button.disabled = false;
            button.removeAttribute("aria-busy");
            button.innerHTML = button.dataset.originalContent || button.innerHTML;
            delete button.dataset.originalContent;
            delete button.dataset.submitLock;
        });
    };

    document.querySelectorAll("form[method='post']").forEach(form => {
        form.addEventListener("invalid", () => restoreSubmissionState(form), true);
        form.addEventListener("submit", event => {
            const jqueryForm = window.jQuery ? window.jQuery(form) : null;
            const unobtrusiveValidationFailed = jqueryForm?.data("validator") && !jqueryForm.valid();
            if (event.defaultPrevented || !form.checkValidity() || unobtrusiveValidationFailed || form.dataset.submitting === "true") {
                if (form.dataset.submitting === "true") event.preventDefault();
                else restoreSubmissionState(form);
                return;
            }

            form.dataset.submitting = "true";
            const submitter = event.submitter;
            if (submitter instanceof HTMLButtonElement) {
                window.requestAnimationFrame(() => {
                    if (event.defaultPrevented) {
                        restoreSubmissionState(form);
                        return;
                    }

                    submitter.dataset.originalContent = submitter.innerHTML;
                    submitter.dataset.submitLock = "true";
                    submitter.disabled = true;
                    submitter.setAttribute("aria-busy", "true");
                    submitter.textContent = "處理中…";
                });
            }
        });
    });

    window.addEventListener("pageshow", () => {
        document.querySelectorAll("form[data-submitting='true']").forEach(restoreSubmissionState);
    });

    document.querySelectorAll("#primaryNavigation .nav-link").forEach(link => {
        link.addEventListener("click", () => {
            const navigation = document.getElementById("primaryNavigation");
            if (!navigation || !navigation.classList.contains("show") || !window.bootstrap) return;
            window.bootstrap.Collapse.getOrCreateInstance(navigation).hide();
        });
    });
})();
