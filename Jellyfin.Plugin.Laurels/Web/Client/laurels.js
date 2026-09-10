/* Laurels – Jellyfin web client additions.
 * Loaded from index.html (via the File Transformation plugin). Runs on the item detail page:
 *  - replaces the star / tomato icons with the logo of the source that produced the value,
 *  - adds a strip with every rating we know, each linking to its source,
 *  - adds award badges when the awards index has something for the item.
 */
(function () {
    'use strict';
    if (window.__laurelsLoaded) { return; }
    window.__laurelsLoaded = true;

    var BASE = (function () {
        var p = window.location.pathname;
        var i = p.toLowerCase().indexOf('/web/');
        return i >= 0 ? p.substring(0, i) : (p.toLowerCase().endsWith('/web') ? p.slice(0, -4) : '');
    })();
    var STYLE_ID = 'laurels-style';
    var settings = null;
    var sourcesByKey = {};
    var currentItemId = null;
    var lastData = {};
    var renderTimer = null;

    function log() { if (window.__laurelsDebug) { console.log.apply(console, ['[Laurels]'].concat([].slice.call(arguments))); } }

    function assetUrl(icon) { return BASE + '/Laurels/Assets/' + icon; }

    function injectStyle() {
        if (document.getElementById(STYLE_ID)) { return; }
        var css = [
            '.laurels-block{display:block;position:relative;width:100%;margin:.2em 0 .6em 0;}',
            '.layout-mobile .laurels-block{text-align:center;}',
            '.layout-mobile .laurels-strip,.layout-mobile .laurels-awards{justify-content:center;}',
            '.laurels-strip{display:flex;flex-wrap:wrap;align-items:center;gap:.9em;margin:.3em 0 0 0;}',
            '.laurels-rating{display:inline-flex;align-items:center;gap:.35em;color:inherit;text-decoration:none;white-space:nowrap;opacity:.95}',
            '.laurels-rating:hover{opacity:1;text-decoration:none}',
            '.laurels-rating img{height:1.35em;width:auto;max-width:2.6em;display:block}',
            '.laurels-rating .laurels-votes{font-size:.75em;opacity:.65;margin-left:.15em}',
            '.laurels-flag{font-size:.7em;padding:.1em .4em;border-radius:.6em;background:rgba(255,255,255,.14);margin-left:.2em}',
            '.laurels-src-icon{height:1.25em;width:auto;vertical-align:middle;margin-right:.3em}',
            '.laurels-awards{display:flex;flex-wrap:wrap;gap:.6em;margin:.2em 0 .4em 0}',
            '.laurels-award{display:inline-flex;align-items:center;gap:.4em;padding:.15em .6em;border-radius:1em;background:rgba(184,134,11,.22);border:1px solid rgba(218,165,32,.5);font-size:.9em;white-space:nowrap}',
            '.laurels-award.nominee{background:rgba(255,255,255,.08);border-color:rgba(255,255,255,.25)}',
            '.laurels-award b{font-weight:600}',
            '.laurels-reviews{margin:.6em 0;font-size:.95em;opacity:.9}',
            '.laurels-review{margin:.3em 0;padding-left:.7em;border-left:2px solid rgba(255,255,255,.25)}',
            '.laurels-review .who{opacity:.65;font-size:.85em}',
            '.laurels-caption{white-space:normal!important;line-height:1.25;font-size:.85em;opacity:.85;max-height:2.6em;overflow:hidden}',
            '.laurels-section .cardText-secondary.laurels-caption{color:#e1bd68}'
        ].join('');
        var style = document.createElement('style');
        style.id = STYLE_ID;
        style.textContent = css;
        document.head.appendChild(style);
    }

    function apiClient() { return window.ApiClient || null; }

    function getJson(path) {
        var client = apiClient();
        if (!client) { return Promise.reject(new Error('no ApiClient')); }
        return client.ajax({ type: 'GET', url: client.getUrl(path), dataType: 'json' });
    }

    function loadSettings() {
        if (settings) { return Promise.resolve(settings); }
        return fetch(BASE + '/Laurels/ClientSettings', { credentials: 'same-origin' }).then(function (r) { return r.json(); }).then(function (s) {
            settings = s;
            (s.sources || []).forEach(function (src) { sourcesByKey[src.key] = src; });
            return s;
        });
    }

    function itemIdFromHash() {
        var h = window.location.hash || '';
        if (h.indexOf('details') < 0) { return null; }
        var q = h.indexOf('?');
        if (q < 0) { return null; }
        var params = new URLSearchParams(h.substring(q + 1));
        return params.get('id');
    }

    function activeDetailPage() {
        var pages = document.querySelectorAll('#itemDetailPage:not(.hide), .itemDetailPage:not(.hide)');
        return pages.length ? pages[pages.length - 1] : null;
    }

    function formatValue(r) {
        var key = r.source;
        var v = r.value;
        if (v == null) { return r.score != null ? String(r.score) : '?'; }
        if (key === 'tomatoes' || key === 'audience' || key === 'trakt') { return Math.round(v) + '%'; }
        if (key === 'metacritic') { return String(Math.round(v)); }
        if ((key === 'tmdb' || key === 'metacritic_user') && v > 10) { v = v / 10; }
        if (key === 'letterboxd') { return (Math.round(v * 10) / 10).toFixed(1) + '/5'; }
        if (key === 'rogerebert') { return (Math.round(v * 10) / 10).toFixed(1) + '/4'; }
        return (Math.round(v * 10) / 10).toFixed(1);
    }

    function formatVotes(v) {
        if (!v) { return ''; }
        if (v >= 1000000) { return (v / 1000000).toFixed(1) + 'M'; }
        if (v >= 1000) { return Math.round(v / 1000) + 'k'; }
        return String(v);
    }

    function buildStrip(data) {
        var order = (settings.ratingsPanelSources && settings.ratingsPanelSources.length) ? settings.ratingsPanelSources : (settings.sources || []).map(function (s) { return s.key; });
        var byKey = {};
        (data.ratings || []).forEach(function (r) { byKey[r.source] = r; });
        var strip = document.createElement('div');
        strip.className = 'laurels-strip';
        order.forEach(function (key) {
            var r = byKey[key];
            if (!r) { return; }
            var src = sourcesByKey[key] || { name: key, icon: null };
            var href = (r.url && /^https?:\/\//i.test(r.url)) ? r.url : null;
            var el = document.createElement(href ? 'a' : 'span');
            el.className = 'laurels-rating';
            el.title = src.name + (r.votes ? ' · ' + r.votes.toLocaleString() + ' votes' : '');
            if (href) { el.href = href; el.target = '_blank'; el.rel = 'noopener noreferrer'; }
            if (src.icon) {
                var img = document.createElement('img');
                img.src = assetUrl(src.icon);
                img.alt = src.name;
                el.appendChild(img);
            } else {
                var name = document.createElement('span');
                name.textContent = src.name;
                el.appendChild(name);
            }
            var val = document.createElement('span');
            val.textContent = formatValue(r);
            el.appendChild(val);
            var votes = formatVotes(r.votes);
            if (votes) {
                var v = document.createElement('span');
                v.className = 'laurels-votes';
                v.textContent = '(' + votes + ')';
                el.appendChild(v);
            }
            strip.appendChild(el);
        });
        (data.flags || []).forEach(function (f) {
            var flag = document.createElement('span');
            flag.className = 'laurels-flag';
            flag.textContent = f.replace(/_/g, ' ');
            strip.appendChild(flag);
        });
        return strip.childNodes.length ? strip : null;
    }

    function replaceIcons(page, data) {
        if (!settings.replaceRatingIcons) { return; }
        var misc = page.querySelector('.itemMiscInfo-primary');
        if (!misc) { return; }
        var community = data.appliedCommunitySource && sourcesByKey[data.appliedCommunitySource];
        var star = misc.querySelector('.starRatingContainer .material-icons, .starRatingContainer .star, .mediaInfoItem .material-icons.star');
        if (community && star && !star.dataset.laurels) {
            var img = document.createElement('img');
            img.className = 'laurels-src-icon';
            img.src = assetUrl(community.icon);
            img.alt = community.name;
            img.title = community.name;
            img.dataset.laurels = '1';
            star.replaceWith(img);
        }
        var critic = data.appliedCriticSource && sourcesByKey[data.appliedCriticSource];
        var criticEl = misc.querySelector('.mediaInfoCriticRating');
        if (critic && criticEl && !criticEl.dataset.laurels) {
            criticEl.dataset.laurels = '1';
            criticEl.classList.remove('mediaInfoCriticRatingFresh', 'mediaInfoCriticRatingRotten');
            var cimg = document.createElement('img');
            cimg.className = 'laurels-src-icon';
            cimg.src = assetUrl(critic.icon);
            cimg.alt = critic.name;
            cimg.title = critic.name;
            criticEl.insertBefore(cimg, criticEl.firstChild);
            criticEl.style.paddingLeft = '0';
            criticEl.style.background = 'none';
        }
    }

    function buildAwards(awards) {
        if (!awards || !awards.entries || !awards.entries.length) { return null; }
        var words = (settings && settings.words) || {};
        var wrap = document.createElement('div');
        wrap.className = 'laurels-awards';
        awards.entries.forEach(function (a) {
            var el = document.createElement('span');
            el.className = 'laurels-award ' + (a.won ? 'winner' : 'nominee');
            el.title = a.categories && a.categories.length ? a.categories.join('\n') : '';
            var icon = document.createElement('img');
            icon.src = assetUrl('award.svg');
            icon.style.height = '1.2em';
            icon.alt = '';
            el.appendChild(icon);
            var text = document.createElement('span');
            var label = a.familyName || a.family;
            var detail;
            if (a.won) {
                var cats = (a.wonCategories || []).slice(0, 2).join(', ');
                var more = (a.wonCategories || []).length - 2;
                detail = cats ? cats + (more > 0 ? ' +' + more : '') : (a.wins > 1 ? a.wins + ' × ' : '') + (words.winner || 'winner');
            } else {
                detail = (a.nominations > 1 ? a.nominations + ' × ' : '') + (words.nominee || 'nominee');
            }
            text.innerHTML = '<b>' + escapeHtml(label) + (a.year ? ' ' + a.year : '') + '</b> · ' + escapeHtml(detail);
            el.appendChild(text);
            wrap.appendChild(el);
        });
        return wrap;
    }

    // ---- Home screen: captions under the cards of our sections, and title links to the collections ----

    var sectionsInfo = null;
    function loadSections() {
        if (sectionsInfo) { return Promise.resolve(sectionsInfo); }
        return getJson('Laurels/Sections').then(function (list) { sectionsInfo = list || []; return sectionsInfo; });
    }

    function decorateHome() {
        var container = document.querySelector('.homeSectionsContainer');
        if (!container) { return; }
        loadSections().then(function (sections) {
            sections.forEach(function (sec) {
                var el = container.querySelector('.verticalSection.' + sec.id);
                if (!el || el.dataset.laurelsDone === '1') { return; }
                var cards = el.querySelectorAll('.card[data-id]');
                if (!cards.length) { return; }
                el.dataset.laurelsDone = '1';
                el.classList.add('laurels-section');
                if (sec.collectionId) {
                    var link = el.querySelector('a.sectionTitleTextButton');
                    if (link) { link.href = '#/details?id=' + sec.collectionId; }
                }
                var ids = [];
                cards.forEach(function (c) { ids.push(c.dataset.id); });
                var client = apiClient();
                client.ajax({ type: 'POST', url: client.getUrl('Laurels/Cards'), dataType: 'json', contentType: 'application/json', data: JSON.stringify({ section: sec.key, itemIds: ids }) }).then(function (res) {
                    var items = (res && res.items) || {};
                    cards.forEach(function (card) {
                        var info = items[card.dataset.id] || items[(card.dataset.id || '').replace(/-/g, '')];
                        if (!info) { return; }
                        var texts = card.querySelectorAll('.cardText');
                        var target = texts.length > 1 ? texts[texts.length - 1] : null;
                        var cap = document.createElement('div');
                        cap.className = 'cardText cardTextCentered cardText-secondary laurels-caption';
                        cap.textContent = info.caption;
                        cap.title = info.tooltip || info.caption;
                        if (target) { target.replaceWith(cap); } else if (texts.length) { texts[0].parentNode.appendChild(cap); }
                    });
                }).catch(function (err) { log('cards failed', err); el.dataset.laurelsDone = ''; });
            });
        }).catch(function () { /* sections unavailable */ });
    }

    function buildReviews(reviews) {
        if (!reviews || !reviews.length) { return null; }
        var wrap = document.createElement('div');
        wrap.className = 'laurels-reviews';
        reviews.slice(0, 3).forEach(function (r) {
            var el = document.createElement('div');
            el.className = 'laurels-review';
            el.innerHTML = '<span>' + escapeHtml(r.excerpt) + '</span> <span class="who"> - ' + escapeHtml(r.author || r.source || '') + (r.rating != null ? ' · ' + r.rating : '') + '</span>';
            wrap.appendChild(el);
        });
        return wrap;
    }

    function escapeHtml(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; });
    }

    function clear(page) {
        page.querySelectorAll('.laurels-block, .laurels-reviews').forEach(function (n) { n.remove(); });
    }

    // On desktop the ribbon holding the misc-info row has a fixed height (7.2em) and centres its
    // content, so anything added inside it overflows and overlaps. The block therefore goes at the
    // top of the detail content column, which already has the same left offset as the ribbon text.
    function getBlock(page, misc) {
        var block = page.querySelector('.laurels-block');
        if (!block) {
            block = document.createElement('div');
            block.className = 'laurels-block';
            // Jellyfin 12 renamed the content column to detailPagePrimaryContent; 10.x used detailPageContent.
            var content = page.querySelector('.detailPagePrimaryContent') || page.querySelector('.detailPageContent');
            if (content) {
                content.insertBefore(block, content.firstChild);
            } else {
                var ribbon = misc.closest('.detailRibbon') || misc;
                ribbon.parentNode.insertBefore(block, ribbon.nextSibling);
            }
        }
        return block;
    }

    function render() {
        var page = activeDetailPage();
        var itemId = itemIdFromHash();
        if (!page || !itemId) { return; }
        var misc = page.querySelector('.itemMiscInfo-primary');
        if (!misc) { scheduleRender(250); return; }
        if (page.dataset.laurelsItem === itemId && page.querySelector('.laurels-block')) {
            // Jellyfin re-renders the misc-info row after the item loads; put the source icons back if it did.
            if (lastData[itemId]) { replaceIcons(page, lastData[itemId]); }
            return;
        }
        page.dataset.laurelsItem = itemId;
        currentItemId = itemId;
        clear(page);

        loadSettings().then(function () {
            return getJson('Laurels/Items/' + itemId);
        }).then(function (data) {
            if (currentItemId !== itemId || !data) { return; }
            lastData[itemId] = data;
            injectStyle();
            var block = getBlock(page, misc);
            if (data.found) {
                replaceIcons(page, data);
                if (settings.showRatingsPanel) {
                    var strip = buildStrip(data);
                    if (strip) { block.appendChild(strip); }
                }
            }
            if (settings.showAwardBadges) {
                getJson('Laurels/Awards/' + itemId).then(function (awards) {
                    if (currentItemId !== itemId) { return; }
                    var el = buildAwards(awards);
                    if (el) { block.insertBefore(el, block.firstChild); }
                }).catch(function () { /* awards index not available */ });
            }
            if (settings.showReviewExcerpts) {
                getJson('Laurels/Reviews/' + itemId).then(function (reviews) {
                    if (currentItemId !== itemId) { return; }
                    var el = buildReviews(reviews && reviews.reviews);
                    var overview = page.querySelector('.detailSectionContent .overview, .overview');
                    if (el && overview) { overview.parentNode.insertBefore(el, overview.nextSibling); }
                }).catch(function () { /* reviews unavailable */ });
            }
        }).catch(function (err) { log('render failed', err); });
    }

    function scheduleRender(delay) {
        clearTimeout(renderTimer);
        renderTimer = setTimeout(render, delay || 120);
    }

    function init() {
        injectStyle();
        document.addEventListener('viewshow', function () { scheduleRender(200); }, true);
        window.addEventListener('hashchange', function () { scheduleRender(300); });
        var homeTimer = null;
        var observer = new MutationObserver(function () {
            var page = activeDetailPage();
            if (page && itemIdFromHash() && !page.querySelector('.laurels-block')) { scheduleRender(150); }
            else if (page && itemIdFromHash() && page.querySelector('.itemMiscInfo-primary .starRatingContainer .material-icons:not([data-laurels]), .itemMiscInfo-primary .mediaInfoCriticRating:not([data-laurels])')) { scheduleRender(150); }
            if (document.querySelector('.homeSectionsContainer .card[data-id]')) {
                clearTimeout(homeTimer);
                homeTimer = setTimeout(decorateHome, 400);
            }
        });
        observer.observe(document.body, { childList: true, subtree: true });
        scheduleRender(500);
        log('ready');
    }

    if (document.readyState === 'loading') { document.addEventListener('DOMContentLoaded', init, { once: true }); } else { init(); }
}());
