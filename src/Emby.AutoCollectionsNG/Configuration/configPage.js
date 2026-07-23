/*
 * Auto Collections NG - configuration page controller (issue #9).
 *
 * Loaded by the Emby dashboard as an AMD module because configPage.html references it via
 * `data-controller="__plugin/autocollectionsngjs"`. Emby resolves that name to
 * `/emby/web/ConfigurationPage?name=autocollectionsngjs` and require()s this file, expecting the
 * module to RETURN a view-controller constructor `function View(view, params)`. The constructor
 * receives the page's root `.view` element; `onResume` runs each time the page is shown.
 *
 * This mechanism (separate data-controller JS module, NOT an inline <script>) was verified against
 * a live Emby 4.9 server on 2026-07-23 by inspecting the shipped MBBackup plugin's config page,
 * whose controller uses exactly this shape (define([...], function (BaseView, ...) { function
 * View(view, params) { BaseView.apply(this, arguments); ... } Object.assign(View.prototype,
 * BaseView.prototype); View.prototype.onResume = ...; return View; })). See
 * docs/emby-api-cheatsheet.md "Configuration UI".
 *
 * The ApiClient methods used here (getPluginConfiguration / updatePluginConfiguration /
 * getScheduledTasks / startScheduledTask) were confirmed present and used the same way by that
 * live reference plugin. ApiClient is a page global in the dashboard context.
 */
define(['baseView'], function (BaseView) {
    'use strict';

    // Must match Plugin.Id in src/Emby.AutoCollectionsNG/Plugin.cs.
    var PLUGIN_ID = '29df6479-d417-492c-8396-1b6a4bca7bb0';

    // Must match AutoCollectionsSyncTask.Key in
    // src/Emby.AutoCollectionsNG/ScheduledTasks/AutoCollectionsSyncTask.cs.
    var TASK_KEY = 'AutoCollectionsNGSync';

    var MATCH_TYPES = ['Regex', 'Contains'];
    var MATCH_FIELDS = ['RawTitle', 'NormalizedTitle', 'FileName'];

    // ---- helpers (pure, no DOM/host state) ---------------------------------------------------

    // Reads a value trying several key spellings, tolerating both PascalCase (C# property names,
    // how Emby serializes plugin-config DTOs to JSON) and camelCase, since we read whatever the
    // host hands back.
    function pick(obj, keys, fallback) {
        if (!obj) {
            return fallback;
        }
        for (var i = 0; i < keys.length; i++) {
            if (obj[keys[i]] !== undefined && obj[keys[i]] !== null) {
                return obj[keys[i]];
            }
        }
        return fallback;
    }

    // Reads an enum-shaped value that might arrive as its string name ("Regex") or numeric index (0).
    function pickEnumName(obj, keys, names) {
        var raw = pick(obj, keys, undefined);
        if (typeof raw === 'string' && names.indexOf(raw) !== -1) {
            return raw;
        }
        if (typeof raw === 'number' && names[raw] !== undefined) {
            return names[raw];
        }
        return names[0];
    }

    function splitCsv(value) {
        if (!value) {
            return [];
        }
        return value
            .split(',')
            .map(function (s) { return s.trim(); })
            .filter(function (s) { return s.length > 0; });
    }

    function joinCsv(arr) {
        if (!arr || !arr.length) {
            return '';
        }
        return arr.join(', ');
    }

    function setStatus(el, kind, message) {
        if (!el) {
            return;
        }
        el.className = el.className.replace(/\s*(ok|error)\b/g, '');
        if (kind) {
            el.className += ' ' + kind;
        }
        el.textContent = message || '';
    }

    // Best-effort human-readable message from whatever error shape the API client rejects with.
    function describeError(err) {
        if (!err) {
            return 'Unknown error.';
        }
        if (typeof err === 'string') {
            return err;
        }
        if (err.statusText || err.status) {
            return (err.status || '') + ' ' + (err.statusText || '');
        }
        if (err.message) {
            return err.message;
        }
        try {
            return JSON.stringify(err);
        } catch (e) {
            return String(err);
        }
    }

    function defaultRule() {
        return {
            CollectionName: '',
            MatchType: 'Contains',
            Pattern: '',
            CaseSensitive: false,
            Enabled: true,
            MatchOn: 'RawTitle',
            AlsoMatchFileName: false,
            LibraryFilter: [],
            ItemTypeFilter: []
        };
    }

    function createSelect(options, selectedValue, cssClass) {
        var select = document.createElement('select');
        select.className = cssClass;
        options.forEach(function (opt) {
            var optionEl = document.createElement('option');
            optionEl.value = opt;
            optionEl.textContent = opt;
            if (opt === selectedValue) {
                optionEl.selected = true;
            }
            select.appendChild(optionEl);
        });
        return select;
    }

    function createTextInput(value, cssClass) {
        var input = document.createElement('input');
        input.type = 'text';
        input.className = cssClass;
        input.value = value || '';
        return input;
    }

    function createCheckbox(checked, cssClass) {
        var input = document.createElement('input');
        input.type = 'checkbox';
        input.className = cssClass;
        input.checked = !!checked;
        return input;
    }

    function cell(child) {
        var td = document.createElement('td');
        td.appendChild(child);
        return td;
    }

    function createRuleRow(rule) {
        var tr = document.createElement('tr');

        var enabled = createCheckbox(pick(rule, ['Enabled', 'enabled'], true), 'acng-enabled');
        var collectionName = createTextInput(pick(rule, ['CollectionName', 'collectionName'], ''), 'acng-collectionName');
        var matchType = createSelect(MATCH_TYPES, pickEnumName(rule, ['MatchType', 'matchType'], MATCH_TYPES), 'acng-matchType');
        var pattern = createTextInput(pick(rule, ['Pattern', 'pattern'], ''), 'acng-pattern');
        var caseSensitive = createCheckbox(pick(rule, ['CaseSensitive', 'caseSensitive'], false), 'acng-caseSensitive');
        var matchOn = createSelect(MATCH_FIELDS, pickEnumName(rule, ['MatchOn', 'matchOn'], MATCH_FIELDS), 'acng-matchOn');
        var alsoMatchFileName = createCheckbox(pick(rule, ['AlsoMatchFileName', 'alsoMatchFileName'], false), 'acng-alsoMatchFileName');
        var libraryFilter = createTextInput(joinCsv(pick(rule, ['LibraryFilter', 'libraryFilter'], [])), 'acng-libraryFilter');
        var itemTypeFilter = createTextInput(joinCsv(pick(rule, ['ItemTypeFilter', 'itemTypeFilter'], [])), 'acng-itemTypeFilter');

        var removeBtn = document.createElement('button');
        removeBtn.type = 'button';
        removeBtn.className = 'acng-remove-btn';
        removeBtn.textContent = String.fromCharCode(0x2716);
        removeBtn.title = 'Remove this rule';
        removeBtn.addEventListener('click', function () {
            tr.parentNode.removeChild(tr);
        });

        tr.appendChild(cell(enabled));
        tr.appendChild(cell(collectionName));
        tr.appendChild(cell(matchType));
        tr.appendChild(cell(pattern));
        tr.appendChild(cell(caseSensitive));
        tr.appendChild(cell(matchOn));
        tr.appendChild(cell(alsoMatchFileName));
        tr.appendChild(cell(libraryFilter));
        tr.appendChild(cell(itemTypeFilter));
        tr.appendChild(cell(removeBtn));

        return tr;
    }

    function renderRules(rulesBody, rules) {
        rulesBody.innerHTML = '';
        (rules || []).forEach(function (rule) {
            rulesBody.appendChild(createRuleRow(rule));
        });
    }

    function collectRulesFromForm(rulesBody) {
        var rows = rulesBody.querySelectorAll('tr');
        var rules = [];
        rows.forEach(function (row) {
            rules.push({
                Enabled: row.querySelector('.acng-enabled').checked,
                CollectionName: row.querySelector('.acng-collectionName').value,
                MatchType: row.querySelector('.acng-matchType').value,
                Pattern: row.querySelector('.acng-pattern').value,
                CaseSensitive: row.querySelector('.acng-caseSensitive').checked,
                MatchOn: row.querySelector('.acng-matchOn').value,
                AlsoMatchFileName: row.querySelector('.acng-alsoMatchFileName').checked,
                LibraryFilter: splitCsv(row.querySelector('.acng-libraryFilter').value),
                ItemTypeFilter: splitCsv(row.querySelector('.acng-itemTypeFilter').value)
            });
        });
        return rules;
    }

    // ---- view controller ---------------------------------------------------------------------

    function View(view, params) {
        BaseView.apply(this, arguments);

        var self = this;

        // Cache the elements this controller works with, scoped to THIS view instance.
        this.statusEl = view.querySelector('.acngStatus');
        this.syncStatusEl = view.querySelector('.acngSyncStatus');
        this.rulesBody = view.querySelector('.acngRulesBody');
        this.deleteEmptyCheckbox = view.querySelector('.acngDeleteEmpty');
        this.loadedConfig = null;

        view.querySelector('.acngAddRuleBtn').addEventListener('click', function () {
            self.rulesBody.appendChild(createRuleRow(defaultRule()));
        });
        view.querySelector('.acngSaveBtn').addEventListener('click', function () {
            self.saveConfig();
        });
        view.querySelector('.acngSyncBtn').addEventListener('click', function () {
            self.triggerSync();
        });
    }

    Object.assign(View.prototype, BaseView.prototype);

    // Emby calls onResume every time the page becomes visible; load fresh config here.
    View.prototype.onResume = function (options) {
        BaseView.prototype.onResume.apply(this, arguments);
        this.loadConfig();
    };

    View.prototype.loadConfig = function () {
        var self = this;
        setStatus(this.statusEl, '', 'Loading configuration...');
        try {
            ApiClient.getPluginConfiguration(PLUGIN_ID).then(
                function (config) {
                    self.loadedConfig = config || {};
                    var rules = pick(self.loadedConfig, ['Rules', 'rules'], []);
                    renderRules(self.rulesBody, rules);
                    self.deleteEmptyCheckbox.checked = !!pick(self.loadedConfig, ['DeleteEmptyCollections', 'deleteEmptyCollections'], false);
                    setStatus(self.statusEl, '', '');
                },
                function (err) {
                    setStatus(self.statusEl, 'error', 'Failed to load configuration: ' + describeError(err));
                }
            );
        } catch (e) {
            setStatus(self.statusEl, 'error', 'Unexpected error loading configuration: ' + describeError(e));
        }
    };

    View.prototype.saveConfig = function () {
        var self = this;
        setStatus(this.statusEl, '', 'Saving...');
        try {
            var config = this.loadedConfig || {};
            config.Rules = collectRulesFromForm(this.rulesBody);
            config.DeleteEmptyCollections = this.deleteEmptyCheckbox.checked;

            ApiClient.updatePluginConfiguration(PLUGIN_ID, config).then(
                function () {
                    self.loadedConfig = config;
                    setStatus(self.statusEl, 'ok', 'Saved.');
                },
                function (err) {
                    setStatus(self.statusEl, 'error', 'Failed to save configuration: ' + describeError(err));
                }
            );
        } catch (e) {
            setStatus(self.statusEl, 'error', 'Unexpected error while saving: ' + describeError(e));
        }
    };

    View.prototype.startTaskById = function (taskId) {
        var self = this;
        try {
            if (ApiClient.startScheduledTask) {
                ApiClient.startScheduledTask(taskId).then(
                    function () { setStatus(self.syncStatusEl, 'ok', 'Sync started.'); },
                    function (err) { setStatus(self.syncStatusEl, 'error', 'Failed to start sync: ' + describeError(err)); }
                );
                return;
            }

            // Fallback: raw REST call to the conventional Emby "run task now" route.
            ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl('ScheduledTasks/Running/' + taskId)
            }).then(
                function () { setStatus(self.syncStatusEl, 'ok', 'Sync started.'); },
                function (err) { setStatus(self.syncStatusEl, 'error', 'Failed to start sync: ' + describeError(err)); }
            );
        } catch (e) {
            setStatus(self.syncStatusEl, 'error', 'Unexpected error starting sync: ' + describeError(e));
        }
    };

    View.prototype.triggerSync = function () {
        var self = this;
        setStatus(this.syncStatusEl, '', 'Starting sync...');
        try {
            // Resolve our task's server-side Id from its stable Key rather than guessing an Id.
            ApiClient.getScheduledTasks().then(
                function (tasks) {
                    var task = (tasks || []).filter(function (t) {
                        return pick(t, ['Key', 'key'], null) === TASK_KEY;
                    })[0];

                    if (!task) {
                        setStatus(self.syncStatusEl, 'error', 'Sync task not found on the server (looked for Key "' + TASK_KEY + '").');
                        return;
                    }

                    var taskId = pick(task, ['Id', 'id'], null);
                    if (!taskId) {
                        setStatus(self.syncStatusEl, 'error', 'Sync task was found but has no Id to start.');
                        return;
                    }

                    self.startTaskById(taskId);
                },
                function (err) {
                    setStatus(self.syncStatusEl, 'error', 'Failed to list scheduled tasks: ' + describeError(err));
                }
            );
        } catch (e) {
            setStatus(self.syncStatusEl, 'error', 'Unexpected error triggering sync: ' + describeError(e));
        }
    };

    return View;
});
