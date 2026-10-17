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

        async function fetchNotifications() {
            try {
                const res = await fetch("/Notifications/GetUnread");
                if (!res.ok) return;
                const data = await res.json();
                const unreadCount = data.count || 0;

                dots.forEach(function (dot) {
                    dot.hidden = unreadCount === 0;
                    dot.classList.toggle("is-hidden", unreadCount === 0);
                });

                if (list) {
                    if (data.items && data.items.length > 0) {
                        list.innerHTML = "";
                        data.items.forEach(function (item) {
                            const a = document.createElement("a");
                            a.href = item.targetUrl || "#";
                            a.className = "profile-notification-item";
                            a.innerHTML = `
                                <div class="profile-notification-title">${item.title}</div>
                                <div class="profile-notification-text">${item.message}</div>
                                <div class="profile-notification-time small text-muted">${item.timeAgo}</div>
                            `;
                            list.appendChild(a);
                        });
                        list.classList.remove("d-none");
                        if (empty) empty.classList.add("d-none");
                    } else {
                        list.classList.add("d-none");
                        if (empty) empty.classList.remove("d-none");
                    }
                }
            } catch (e) {
                // Background notification poll fallback
            }
        }

        fetchNotifications();

        document.querySelectorAll("[data-notification-toggle], .profile-button").forEach(function (btn) {
            btn.addEventListener("click", function () {
                dots.forEach(function (dot) {
                    dot.hidden = true;
                    dot.classList.add("is-hidden");
                });
                fetch("/Notifications/MarkAllAsRead", { method: "POST" }).catch(() => {});
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

    function initKanban() {
        const board = document.querySelector(".kanban-board");
        if (!board) return;

        let draggedCard = null;

        board.querySelectorAll(".kanban-card").forEach(function (card) {
            card.addEventListener("dragstart", function (e) {
                draggedCard = card;
                card.classList.add("is-dragging");
                e.dataTransfer.setData("text/plain", card.getAttribute("data-application-id") || "");
                e.dataTransfer.effectAllowed = "move";
            });

            card.addEventListener("dragend", function () {
                card.classList.remove("is-dragging");
                draggedCard = null;
                board.querySelectorAll(".kanban-column-body").forEach(col => col.classList.remove("drop-active"));
            });
        });

        board.querySelectorAll(".kanban-column-body[data-dropzone]").forEach(function (col) {
            col.addEventListener("dragover", function (e) {
                e.preventDefault();
                e.dataTransfer.dropEffect = "move";
                col.classList.add("drop-active");
            });

            col.addEventListener("dragleave", function (e) {
                if (!col.contains(e.relatedTarget)) {
                    col.classList.remove("drop-active");
                }
            });

            col.addEventListener("drop", async function (e) {
                e.preventDefault();
                col.classList.remove("drop-active");

                if (!draggedCard) return;

                const targetStatus = col.getAttribute("data-status");
                const stageIdAttr = col.getAttribute("data-stage-id");
                const stageId = stageIdAttr ? parseInt(stageIdAttr, 10) : null;
                const currentStatus = draggedCard.getAttribute("data-status");
                const appId = parseInt(draggedCard.getAttribute("data-application-id"), 10);

                if (targetStatus === currentStatus) return;

                const sourceCol = draggedCard.closest(".kanban-column-body");

                // Optimistically move card
                col.appendChild(draggedCard);
                draggedCard.setAttribute("data-status", targetStatus);

                updateColumnCounters();

                try {
                    const response = await fetch("/Applications/UpdateStatusAjax", {
                        method: "POST",
                        headers: { "Content-Type": "application/json" },
                        body: JSON.stringify({ id: appId, status: targetStatus, stageId: stageId })
                    });

                    if (!response.ok) throw new Error("Помилка оновлення статусу");
                    const data = await response.json();
                    if (!data.success) throw new Error(data.message || "Не вдалося зберегти");

                    draggedCard.style.borderColor = "var(--primary-color)";
                    setTimeout(() => { draggedCard.style.borderColor = ""; }, 800);
                } catch (err) {
                    alert("Не вдалося оновити статус: " + err.message);
                    if (sourceCol) {
                        sourceCol.appendChild(draggedCard);
                        draggedCard.setAttribute("data-status", currentStatus);
                        updateColumnCounters();
                    }
                }
            });
        });

        function updateColumnCounters() {
            board.querySelectorAll(".kanban-column").forEach(function (col) {
                const cards = col.querySelectorAll(".kanban-card");
                const badge = col.querySelector(".kanban-count");
                const empty = col.querySelector(".kanban-empty");

                if (badge) badge.textContent = cards.length.toString();
                if (empty) empty.classList.toggle("d-none", cards.length > 0);
            });
        }

        // SignalR Real-Time Kanban Synchronizer
        if (window.signalR) {
            try {
                const connection = new signalR.HubConnectionBuilder()
                    .withUrl("/hubs/kanban")
                    .withAutomaticReconnect()
                    .build();

                connection.on("ApplicationMoved", function (data) {
                    if (!data || !data.id) return;
                    const card = board.querySelector(`.kanban-card[data-application-id="${data.id}"]`);
                    const targetCol = board.querySelector(`.kanban-column-body[data-status="${data.status}"]`);
                    if (card && targetCol && card.getAttribute("data-status") !== data.status) {
                        targetCol.appendChild(card);
                        card.setAttribute("data-status", data.status);
                        updateColumnCounters();
                        card.style.transition = "transform 0.3s ease, border-color 0.3s ease";
                        card.style.borderColor = "var(--primary-color)";
                        setTimeout(() => { card.style.borderColor = ""; }, 1200);
                    }
                });

                connection.on("ApplicationCreated", function (data) {
                    updateColumnCounters();
                });

                connection.start().then(function () {
                    const select = document.querySelector('select[name="vacancyId"]');
                    const vacId = select ? select.value : "0";
                    connection.invoke("JoinVacancyBoard", vacId || "0");
                }).catch(function (err) {
                    console.warn("SignalR connection skipped:", err);
                });
            } catch (err) {
                console.warn("SignalR init skipped:", err);
            }
        }
    }

    function initQuickView() {
        const drawerEl = document.getElementById("quickViewDrawer");
        if (!drawerEl) return;

        let currentCandidateId = null;

        document.addEventListener("click", async function (e) {
            const btn = e.target.closest("[data-quick-view]");
            if (!btn) return;

            e.preventDefault();
            const id = btn.getAttribute("data-quick-view");
            if (!id) return;

            currentCandidateId = parseInt(id, 10);

            try {
                const res = await fetch(`/Candidates/QuickViewJson?id=${id}`);
                if (!res.ok) throw new Error("Кандидата не знайдено");
                const data = await res.json();
                if (!data.success) throw new Error(data.message);

                const title = document.getElementById("quickViewDrawerLabel");
                const subtitle = document.getElementById("qvSubtitle");
                if (title) title.textContent = data.fullName;
                if (subtitle) subtitle.textContent = (data.skills ? data.skills.split(',').slice(0, 3).join(', ') : "Кандидат кадрового резерву");

                const initial = document.getElementById("qvAvatarInitial");
                const avatarImg = document.getElementById("qvAvatarImg");
                if (data.avatarUrl) {
                    if (avatarImg) {
                        avatarImg.src = data.avatarUrl;
                        avatarImg.alt = data.fullName;
                        avatarImg.classList.remove("d-none");
                    }
                    if (initial) initial.classList.add("d-none");
                } else {
                    if (avatarImg) avatarImg.classList.add("d-none");
                    if (initial) {
                        initial.textContent = data.fullName ? data.fullName[0].toUpperCase() : "К";
                        initial.classList.remove("d-none");
                    }
                }

                const exp = document.getElementById("qvExperience");
                if (exp) exp.textContent = data.experienceYears > 0 ? `${data.experienceYears} р.` : "Без досвіду";

                const softScore = document.getElementById("qvSoftSkillScore");
                if (softScore) softScore.textContent = data.softSkills ? `${data.softSkills.average} / 10` : "Не оцінено";

                const emailLink = document.getElementById("qvEmailLink");
                if (emailLink) {
                    emailLink.href = `mailto:${data.email}`;
                    emailLink.textContent = data.email;
                }
                const phone = document.getElementById("qvPhone");
                if (phone) phone.textContent = data.phone || "Не вказано";

                const skillsContainer = document.getElementById("qvSkillsList");
                if (skillsContainer) {
                    skillsContainer.innerHTML = "";
                    if (data.skills) {
                        data.skills.split(/[,;]/).forEach(s => {
                            const trimmed = s.trim();
                            if (trimmed) {
                                const span = document.createElement("span");
                                span.className = "badge-soft badge-soft-emerald";
                                span.textContent = trimmed;
                                skillsContainer.appendChild(span);
                            }
                        });
                    } else {
                        skillsContainer.innerHTML = '<span class="text-muted small">Навички не вказані</span>';
                    }
                }

                const resumeCard = document.getElementById("qvResumeCard");
                const resumeDownloadBtn = document.getElementById("qvResumeDownloadBtn");
                const resumeSummary = document.getElementById("qvResumeSummary");
                if (resumeCard && resumeDownloadBtn) {
                    if (data.hasResume) {
                        resumeCard.classList.remove("d-none");
                        resumeDownloadBtn.href = data.resumeFilePath;
                        if (resumeSummary) resumeSummary.textContent = data.resumeSummary ? "Резюме розпізнано системою" : "Файл прикріплено";
                    } else {
                        resumeCard.classList.add("d-none");
                    }
                }

                const appsContainer = document.getElementById("qvApplicationsList");
                if (appsContainer) {
                    appsContainer.innerHTML = "";
                    if (data.applications && data.applications.length > 0) {
                        data.applications.forEach(app => {
                            const item = document.createElement("div");
                            item.className = "p-2 bg-surface border rounded-3 d-flex align-items-center justify-content-between";
                            item.innerHTML = `
                                <div>
                                    <strong class="d-block small text-truncate" style="max-width: 220px;">${app.vacancyTitle}</strong>
                                    <span class="small text-muted">Подано: ${app.appliedAt}</span>
                                </div>
                                <span class="${app.statusBadge}">${app.statusLabel}</span>
                            `;
                            appsContainer.appendChild(item);
                        });
                    } else {
                        appsContainer.innerHTML = '<span class="text-muted small">Немає активних заявок</span>';
                    }
                }

                const intContainer = document.getElementById("qvInterviewsList");
                if (intContainer) {
                    intContainer.innerHTML = "";
                    if (data.interviews && data.interviews.length > 0) {
                        data.interviews.forEach(i => {
                            const item = document.createElement("div");
                            item.className = "p-2 bg-surface border rounded-3 d-flex align-items-center justify-content-between";
                            item.innerHTML = `
                                <div>
                                    <strong class="d-block small">${i.interviewTypeLabel} · ${i.interviewDate}</strong>
                                    <span class="small text-muted">${i.recruiterName}</span>
                                </div>
                                <div class="d-flex align-items-center gap-1">
                                    <span class="${i.resultBadge}">${i.result}</span>
                                    ${i.locationOrMeetingLink ? `<a href="${i.locationOrMeetingLink}" target="_blank" class="btn btn-xs btn-outline-success">Дзвінок</a>` : ''}
                                </div>
                            `;
                            intContainer.appendChild(item);
                        });
                    } else {
                        intContainer.innerHTML = '<span class="text-muted small">Співбесіди ще не призначені</span>';
                    }
                }

                const commContainer = document.getElementById("qvCommunicationsList");
                const commCount = document.getElementById("qvCommCount");
                if (commContainer) {
                    commContainer.innerHTML = "";
                    if (data.communications && data.communications.length > 0) {
                        if (commCount) commCount.textContent = `${data.communications.length} листів`;
                        data.communications.forEach(c => {
                            const item = document.createElement("div");
                            item.className = "p-2 bg-surface border rounded-3";
                            item.innerHTML = `
                                <div class="d-flex justify-content-between align-items-center mb-1">
                                    <span class="badge-soft badge-soft-slate">${c.channel} · ${c.status}</span>
                                    <span class="small text-muted font-monospace">${c.sentAt}</span>
                                </div>
                                <strong class="d-block small text-truncate mb-1">${c.subject}</strong>
                                <div class="small text-muted" style="white-space: pre-wrap; max-height: 55px; overflow-y: auto; font-size: 0.8rem;">${c.body}</div>
                            `;
                            commContainer.appendChild(item);
                        });
                    } else {
                        if (commCount) commCount.textContent = "0 листів";
                        commContainer.innerHTML = '<span class="text-muted small">Немає зафіксованих звернень</span>';
                    }
                }

                const notesTimeline = document.getElementById("qvNotesTimeline");
                if (notesTimeline) {
                    const comments = data.applications.map(a => a.recruiterComment).filter(c => c && c.trim()).join("\n\n");
                    notesTimeline.textContent = comments || (data.resumeSummary || "Немає збережених нотаток.");
                }

                const fullBtn = document.getElementById("qvFullDetailsBtn");
                if (fullBtn) fullBtn.href = `/Candidates/Details/${data.id}`;

                const outreachTriggerBtn = document.getElementById("qvOutreachTriggerBtn");
                if (outreachTriggerBtn) {
                    outreachTriggerBtn.setAttribute("data-candidate-id", data.id);
                    outreachTriggerBtn.setAttribute("data-candidate-name", data.fullName);
                    outreachTriggerBtn.setAttribute("data-candidate-email", data.email);
                    outreachTriggerBtn.setAttribute("data-vacancy", data.applications.length > 0 ? data.applications[0].vacancyTitle : "");
                }

                if (window.bootstrap && bootstrap.Offcanvas) {
                    const bsOffcanvas = bootstrap.Offcanvas.getOrCreateInstance(drawerEl);
                    bsOffcanvas.show();
                }
            } catch (err) {
                alert("Помилка завантаження даних: " + err.message);
            }
        });

        const saveNoteBtn = document.getElementById("qvSaveNoteBtn");
        const newNoteInput = document.getElementById("qvNewNoteInput");
        if (saveNoteBtn && newNoteInput) {
            saveNoteBtn.addEventListener("click", async function () {
                const note = newNoteInput.value.trim();
                if (!note || !currentCandidateId) return;

                saveNoteBtn.disabled = true;
                try {
                    const res = await fetch("/Candidates/AddNoteAjax", {
                        method: "POST",
                        headers: { "Content-Type": "application/json" },
                        body: JSON.stringify({ candidateId: currentCandidateId, note: note })
                    });
                    if (!res.ok) throw new Error("Помилка збереження");
                    const result = await res.json();
                    if (!result.success) throw new Error(result.message);

                    const timeline = document.getElementById("qvNotesTimeline");
                    if (timeline) {
                        timeline.textContent = result.note + "\n\n" + timeline.textContent;
                    }
                    newNoteInput.value = "";
                } catch (e) {
                    alert("Не вдалося зберегти нотатку: " + e.message);
                } finally {
                    saveNoteBtn.disabled = false;
                }
            });
        }
    }

    function initOutreachModal() {
        const modalEl = document.getElementById("outreachModal");
        if (!modalEl) return;

        let activeCandidateId = null;
        let activeName = "";
        let activeEmail = "";
        let activeVacancy = "";
        let activeType = "screening";

        const templates = {
            screening: {
                subject: (name, vac) => `HR-скринінг на позицію ${vac || 'в компанії'} — ${name}`,
                body: (name, vac) => `Доброго дня, ${name}!\n\nМене звати Рекрутер, команда HR Reserve System.\nМи уважно переглянули ваш профіль на позицію ${vac || 'у нашій компанії'} і були б дуже раді поспілкуватися на короткому 20-хвилинному онлайн-знайомстві.\n\nПідкажіть, будь ласка, у які дні та години вам зручно зідзвонитися цього тижня?\n\nЗ повагою,\nКоманда рекрутингу`
            },
            technical: {
                subject: (name, vac) => `Запрошення на технічну співбесіду: ${vac || 'позиція'} — ${name}`,
                body: (name, vac) => `Вітаємо, ${name}!\n\nДякуємо за успішне проходження першого етапу знайомства.\nЗ радістю запрошуємо вас на наступний етап — технічну співбесіду з нашими інженерами за напрямком ${vac || 'проєкту'}.\n\nФормат: онлайн-зустріч (60 хвилин), обговорення вашого досвіду та практичних кейсів.\nОберіть зручний для вас слот у відповіді на цей лист або повідомте ваш графік.\n\nДо зустрічі!`
            },
            offer: {
                subject: (name, vac) => `Офіційна пропозиція про співпрацю (Job Offer) — ${vac || 'HR Reserve System'}!`,
                body: (name, vac) => `Шановний(-а) ${name}!\n\nВся наша команда була приємно вражена спілкуванням з вами на всіх етапах відбору.\nМи щиро раді зробити вам офіційну пропозицію про роботу (Job Offer) на позицію ${vac || 'фахівця'}!\n\nМи впевнені, що разом досягнемо визначних результатів. Деталі компенсаційного пакету та дати старту узгодимо у зручний для вас час.\n\nЛаскаво просимо на борт!`
            },
            rejection: {
                subject: (name, vac) => `Щодо вашого відгуку на позицію ${vac || 'у нашій компанії'}`,
                body: (name, vac) => `Вітаємо, ${name}.\n\nЩиро дякуємо вам за приділений час, інтерес до нашої компанії та відкрите спілкування на позицію ${vac || 'у команді'}.\n\nНаразі ми вирішили рухатися далі з кандидатом, чий поточний практичний досвід точніше відповідає завданням саме цього проєкту на старті.\nВодночас ми високо оцінили ваш професійний рівень та зберегли ваше резюме в нашому кадровому резерві. Якщо у нас з'являться позиції під ваш стек, ми обов'язково зв'яжемося з вами першочергово!\n\nБажаємо успіхів та кар'єрного зростання!`
            }
        };

        function updateModalContent() {
            const tpl = templates[activeType] || templates.screening;
            const subjectInput = document.getElementById("outreachSubjectInput");
            const bodyArea = document.getElementById("outreachBodyTextarea");
            const mailtoBtn = document.getElementById("outreachMailtoBtn");
            const tgBtn = document.getElementById("outreachTelegramBtn");

            const subj = tpl.subject(activeName, activeVacancy);
            const text = tpl.body(activeName, activeVacancy);

            if (subjectInput) subjectInput.value = subj;
            if (bodyArea) bodyArea.value = text;

            if (mailtoBtn) {
                mailtoBtn.href = `mailto:${encodeURIComponent(activeEmail)}?subject=${encodeURIComponent(subj)}&body=${encodeURIComponent(text)}`;
            }
            if (tgBtn) {
                tgBtn.href = `https://t.me/share/url?url=${encodeURIComponent(text)}`;
            }
        }

        document.addEventListener("click", function (e) {
            const btn = e.target.closest("[data-outreach-btn], #qvOutreachTriggerBtn");
            if (!btn) return;

            const cid = btn.getAttribute("data-candidate-id");
            activeCandidateId = cid ? parseInt(cid, 10) : null;
            activeName = btn.getAttribute("data-candidate-name") || "Кандидате";
            activeEmail = btn.getAttribute("data-candidate-email") || "";
            activeVacancy = btn.getAttribute("data-vacancy") || "";
            activeType = "screening";

            const sub = document.getElementById("outreachCandidateSubtitle");
            if (sub) sub.textContent = `${activeName} · ${activeEmail || 'пошта не вказана'}`;

            modalEl.querySelectorAll(".outreach-tab-btn").forEach(b => {
                b.classList.toggle("active", b.getAttribute("data-template-type") === "screening");
            });

            updateModalContent();

            if (window.bootstrap && bootstrap.Modal) {
                const bsModal = bootstrap.Modal.getOrCreateInstance(modalEl);
                bsModal.show();
            }
        });

        modalEl.querySelectorAll(".outreach-tab-btn").forEach(function (tab) {
            tab.addEventListener("click", function () {
                modalEl.querySelectorAll(".outreach-tab-btn").forEach(b => b.classList.remove("active"));
                tab.classList.add("active");
                activeType = tab.getAttribute("data-template-type");
                updateModalContent();
            });
        });

        const copyBtn = document.getElementById("outreachCopyBtn");
        const copyText = document.getElementById("outreachCopyBtnText");
        if (copyBtn) {
            copyBtn.addEventListener("click", function () {
                const bodyArea = document.getElementById("outreachBodyTextarea");
                if (bodyArea) {
                    navigator.clipboard.writeText(bodyArea.value).then(() => {
                        if (copyText) copyText.textContent = "Скопійовано!";
                        setTimeout(() => { if (copyText) copyText.textContent = "Скопіювати"; }, 1500);
                    });
                }
            });
        }

        const sendDirectBtn = document.getElementById("outreachSendDirectBtn");
        const sendDirectText = document.getElementById("outreachSendDirectText");
        if (sendDirectBtn) {
            sendDirectBtn.addEventListener("click", async function () {
                const subjectInput = document.getElementById("outreachSubjectInput");
                const bodyArea = document.getElementById("outreachBodyTextarea");
                if (!subjectInput || !bodyArea) return;

                if (!activeCandidateId) {
                    alert("Оберіть кандидата для відправки листа.");
                    return;
                }

                const subj = subjectInput.value.trim();
                const text = bodyArea.value.trim();
                if (!subj || !text) {
                    alert("Тема та тіло листа обов'язкові для заповнення.");
                    return;
                }

                sendDirectBtn.disabled = true;
                if (sendDirectText) sendDirectText.textContent = "Надсилання...";

                try {
                    const res = await fetch("/Candidates/SendMessageAjax", {
                        method: "POST",
                        headers: { "Content-Type": "application/json" },
                        body: JSON.stringify({
                            candidateId: activeCandidateId,
                            subject: subj,
                            body: text,
                            channel: "Email",
                            templateType: activeType
                        })
                    });

                    if (!res.ok) throw new Error("Помилка надсилання повідомлення");
                    const result = await res.json();
                    if (!result.success) throw new Error(result.message || "Не вдалося надіслати");

                    if (sendDirectText) sendDirectText.textContent = "Надіслано!";
                    setTimeout(() => {
                        if (sendDirectText) sendDirectText.textContent = "Надіслати лист";
                        sendDirectBtn.disabled = false;
                        if (window.bootstrap && bootstrap.Modal) {
                            const bsModal = bootstrap.Modal.getInstance(modalEl);
                            if (bsModal) bsModal.hide();
                        }
                    }, 800);
                } catch (err) {
                    alert("Не вдалося надіслати повідомлення: " + err.message);
                    sendDirectBtn.disabled = false;
                    if (sendDirectText) sendDirectText.textContent = "Надіслати лист";
                }
            });
        }
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
        initKanban();
        initQuickView();
        initOutreachModal();
    });
})();
