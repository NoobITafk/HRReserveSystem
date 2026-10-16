(function () {
    const root = document.documentElement;
    const themeStorageKey = "hrreserve-theme";
    const sidebarStorageKey = "hrreserve-sidebar-collapsed";
    const notificationsStorageKey = "hrreserve-notifications-read";

    function safeGet(key) {
        try {
            return localStorage.getItem(key);
        } catch (error) {
            return null;
        }
    }

    function safeSet(key, value) {
        try {
            localStorage.setItem(key, value);
        } catch (error) {
            // Persistence is a progressive enhancement.
        }
    }

    function normalizeTheme(theme) {
        return theme === "dark" ? "dark" : "light";
    }

    function getSavedTheme() {
        return normalizeTheme(safeGet(themeStorageKey));
    }

    const sunSvg = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="4"/><path d="M12 2v2"/><path d="M12 20v2"/><path d="m4.93 4.93 1.41 1.41"/><path d="m17.66 17.66 1.41 1.41"/><path d="M2 12h2"/><path d="M20 12h2"/><path d="m6.34 17.66-1.41 1.41"/><path d="m19.07 4.93-1.41 1.41"/></svg>';
    const moonSvg = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9Z"/></svg>';

    function applyTheme(theme) {
        const normalizedTheme = normalizeTheme(theme);
        const isDark = normalizedTheme === "dark";

        root.setAttribute("data-theme", normalizedTheme);
        if (document.body) {
            document.body.setAttribute("data-theme", normalizedTheme);
        }

        document.querySelectorAll("[data-theme-toggle]").forEach(function (toggle) {
            const icon = toggle.querySelector(".theme-toggle-icon");
            const text = toggle.querySelector(".theme-toggle-text");

            toggle.setAttribute("aria-pressed", isDark.toString());
            if (icon) {
                icon.innerHTML = isDark ? sunSvg : moonSvg;
            }
            if (text) {
                text.textContent = isDark ? "Світла" : "Темна";
            }
        });
    }

    function initThemeToggles() {
        document.querySelectorAll("[data-theme-toggle]").forEach(function (toggle) {
            toggle.addEventListener("click", function () {
                const nextTheme = root.getAttribute("data-theme") === "dark" ? "light" : "dark";
                safeSet(themeStorageKey, nextTheme);
                applyTheme(nextTheme);
            });
        });
    }

    function initSidebar() {
        const toggles = document.querySelectorAll("[data-sidebar-toggle]");
        const sidebar = document.querySelector(".app-sidebar");

        if (!toggles.length || !sidebar || !document.body) {
            return;
        }

        function isDesktop() {
            return window.matchMedia("(min-width: 992px)").matches;
        }

        function applySidebarState(collapsed) {
            const shouldCollapse = collapsed && isDesktop();
            document.body.classList.toggle("sidebar-collapsed", shouldCollapse);

            toggles.forEach(function (toggle) {
                toggle.setAttribute("aria-expanded", (!shouldCollapse).toString());
                toggle.classList.toggle("is-active", shouldCollapse);
            });
        }

        applySidebarState(safeGet(sidebarStorageKey) === "true");

        toggles.forEach(function (toggle) {
            toggle.addEventListener("click", function () {
                const nextCollapsed = !document.body.classList.contains("sidebar-collapsed");
                safeSet(sidebarStorageKey, nextCollapsed.toString());
                applySidebarState(nextCollapsed);
            });
        });

        window.addEventListener("resize", function () {
            applySidebarState(safeGet(sidebarStorageKey) === "true");
        });
    }

    function closeDropdowns(except) {
        document.querySelectorAll("[data-dropdown]").forEach(function (dropdown) {
            if (dropdown === except) {
                return;
            }

            dropdown.classList.remove("is-open");
            const toggle = dropdown.querySelector("[data-dropdown-toggle]");
            if (toggle) {
                toggle.setAttribute("aria-expanded", "false");
            }
        });
    }

    function initDropdowns() {
        document.querySelectorAll("[data-dropdown]").forEach(function (dropdown) {
            const toggle = dropdown.querySelector("[data-dropdown-toggle]");
            if (!toggle) {
                return;
            }

            toggle.addEventListener("click", function (event) {
                event.preventDefault();
                event.stopPropagation();

                const willOpen = !dropdown.classList.contains("is-open");
                closeDropdowns(dropdown);
                dropdown.classList.toggle("is-open", willOpen);
                toggle.setAttribute("aria-expanded", willOpen.toString());
            });
        });

        document.addEventListener("click", function (event) {
            if (!event.target.closest("[data-dropdown]")) {
                closeDropdowns();
            }
        });

        document.addEventListener("keydown", function (event) {
            if (event.key === "Escape") {
                closeDropdowns();
            }
        });
    }

    function initNotifications() {
        const dots = document.querySelectorAll("[data-notification-dot]");
        const list = document.querySelector("[data-notification-list]");
        const empty = document.querySelector("[data-notification-empty]");

        function syncDot() {
            const isRead = safeGet(notificationsStorageKey) === "true";
            dots.forEach(function (dot) {
                dot.hidden = isRead;
                dot.classList.toggle("is-hidden", isRead);
            });
        }

        if (list && empty) {
            const hasMessages = list.querySelectorAll(".profile-notification-item, .notification-item").length > 0;
            list.classList.toggle("d-none", !hasMessages);
            empty.classList.toggle("d-none", hasMessages);
        }

        syncDot();

        document.querySelectorAll("[data-notification-toggle], .profile-button").forEach(function (btn) {
            btn.addEventListener("click", function () {
                safeSet(notificationsStorageKey, "true");
                syncDot();
            });
        });
    }

    function initGlobalSearch() {
        document.querySelectorAll("[data-global-search]").forEach(function (form) {
            form.addEventListener("submit", function (event) {
                const input = form.querySelector('input[type="search"]');
                if (!input || input.value.trim()) {
                    return;
                }

                event.preventDefault();
                input.focus();
            });
        });
    }

    function initDemoLoginFill() {
        const loginInput = document.getElementById("Login");
        const passwordInput = document.getElementById("Password");

        if (!loginInput || !passwordInput) {
            return;
        }

        document.querySelectorAll("[data-demo-login][data-demo-password]").forEach(function (button) {
            button.addEventListener("click", function () {
                loginInput.value = button.getAttribute("data-demo-login") || "";
                passwordInput.value = button.getAttribute("data-demo-password") || "";
                loginInput.dispatchEvent(new Event("input", { bubbles: true }));
                passwordInput.dispatchEvent(new Event("input", { bubbles: true }));

                if (button.getAttribute("data-autologin") === "true" || button.classList.contains("demo-autologin")) {
                    const form = button.closest("form") || document.getElementById("loginForm") || document.querySelector("form[action*='Login']") || document.querySelector("form");
                    if (form) {
                        form.submit();
                        return;
                    }
                }
                passwordInput.focus();
            });
        });
    }

    function initPasswordToggles() {
        document.querySelectorAll("[data-password-toggle]").forEach(function (button) {
            button.addEventListener("click", function () {
                const wrapper = button.closest(".password-field-group") || button.parentElement;
                if (!wrapper) {
                    return;
                }
                const input = wrapper.querySelector("input");
                if (!input) {
                    return;
                }
                const isPassword = input.getAttribute("type") === "password";
                input.setAttribute("type", isPassword ? "text" : "password");
                button.classList.toggle("is-visible", isPassword);
            });
        });
    }

    function initToasts() {
        document.querySelectorAll("[data-toast-close]").forEach(function (button) {
            button.addEventListener("click", function () {
                const toast = button.closest(".app-toast");
                if (toast) {
                    toast.remove();
                }
            });
        });

        document.querySelectorAll("[data-auto-dismiss]").forEach(function (toast) {
            const delay = parseInt(toast.getAttribute("data-auto-dismiss") || "5000", 10);
            setTimeout(function () {
                if (toast.isConnected) {
                    toast.style.opacity = "0";
                    setTimeout(function () {
                        if (toast.isConnected) {
                            toast.remove();
                        }
                    }, 250);
                }
            }, delay);
        });
    }

    function initClickableRows() {
        document.querySelectorAll("[data-row-link]").forEach(function (row) {
            function openDetails() {
                const url = row.getAttribute("data-row-link");
                if (url) {
                    window.location.href = url;
                }
            }

            row.addEventListener("click", function (event) {
                if (event.target.closest("a, button, input, select, textarea, label, form")) {
                    return;
                }

                openDetails();
            });

            row.addEventListener("keydown", function (event) {
                if (event.key !== "Enter" && event.key !== " ") {
                    return;
                }

                if (event.target.closest("a, button, input, select, textarea, label, form")) {
                    return;
                }

                event.preventDefault();
                openDetails();
            });
        });
    }

    applyTheme(getSavedTheme());

    document.addEventListener("DOMContentLoaded", function () {
        applyTheme(getSavedTheme());
        initThemeToggles();
        initSidebar();
        initDropdowns();
        initNotifications();
        initGlobalSearch();
        initDemoLoginFill();
        initPasswordToggles();
        initToasts();
        initClickableRows();
    });
})();
