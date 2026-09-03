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
    var renderTimer = null;

    function log() { if (window.__laurelsDebug) { console.log.apply(console, ['[Laurels]'].concat([].slice.call(arguments))); } }

    function assetUrl(icon) { return BASE + '/Laurels/Assets/' + icon; }

    function injectStyle() {
        if (document.getElementById(STYLE_ID)) { return; }
        var css = [
            '.laurels-strip{display:flex;flex-wrap:wrap;align-items:center;gap:.9em;margin:.35em 0 .1em 0;}',
            '.laurels-rating{display:inline-flex;align-items:center;gap:.35em;color:inherit;text-decoration:none;white-space:nowrap;opacity:.95}',
            '.laurels-rating:hover{opacity:1;text-decoration:none}',
            '.laurels-rating img{height:1.35em;width:auto;max-width:2.6em;display:block}',
            '.laurels-rating .laurels-votes{font-size:.75em;opacity:.65;margin-left:.15em}',
            '.laurels-flag{font-size:.7em;padding:.1em .4em;border-radius:.6em;background:rgba(255,255,255,.14);margin-left:.2em}',
            '.laurels-src-icon{height:1.25em;width:auto;vertical-align:middle;margin-right:.3em}',
            '.laurels-awards{display:flex;flex-wrap:wrap;gap:.6em;margin:.3em 0}',
            '.laurels-award{display:inline-flex;align-items:center;gap:.4em;padding:.15em .6em;border-radius:1em;background:rgba(184,134,11,.22);border:1px solid rgba(218,165,32,.5);font-size:.9em;white-space:nowrap}',
            '.laurels-award.nominee{background:rgba(255,255,255,.08);border-color:rgba(255,255,255,.25)}',
            '.laurels-award b{font-weight:600}',
            '.laurels-reviews{margin:.6em 0;font-size:.95em;opacity:.9}',
            '.laurels-review{margin:.3em 0;padding-left:.7em;border-left:2px solid rgba(255,255,255,.25)}',
            '.laurels-review .who{opacity:.65;font-size:.85em}'
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
        var src = sourcesByKey[r.source] || { max: 10 };
        if (r.value == null) { return r.score != null ? String(r.score) : '?'; }
        if (src.max === 100) { return Math.round(r.value) + '%'; }
        if (src.max === 5 || src.max === 4) { return (Math.round(r.value * 10) / 10) + '/' + src.max; }
        return (Math.round(r.value * 10) / 10).toFixed(1);
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
            var el = document.createElement(r.url ? 'a' : 'span');
            el.className = 'laurels-rating';
            el.title = src.name + (r.votes ? ' · ' + r.votes.toLocaleString() + ' votes' : '');
            if (r.url) { el.href = r.url; el.target = '_blank'; el.rel = 'noopener noreferrer'; }
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
            var count = a.won ? a.wins : a.nominations;
            text.innerHTML = '<b>' + escapeHtml(a.family) + '</b> ' + (a.won ? (count > 1 ? count + ' wins' : 'winner') : (count > 1 ? count + ' nominations' : 'nominee')) + (a.year ? ' · ' + a.year : '');
            el.appendChild(text);
            wrap.appendChild(el);
        });
        return wrap;
    }

    function buildReviews(reviews) {
        if (!reviews || !reviews.length) { return null; }
        var wrap = document.createElement('div');
        wrap.className = 'laurels-reviews';
        reviews.slice(0, 3).forEach(function (r) {
            var el = document.createElement('div');
            el.className = 'laurels-review';
            el.innerHTML = '<span>' + escapeHtml(r.excerpt) + '</span> <span class="who">— ' + escapeHtml(r.author || r.source || '') + (r.rating != null ? ' · ' + r.rating : '') + '</span>';
            wrap.appendChild(el);
        });
        return wrap;
    }

    function escapeHtml(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; });
    }

    function clear(page) {
        page.querySelectorAll('.laurels-strip, .laurels-awards, .laurels-reviews').forEach(function (n) { n.remove(); });
    }

    function render() {
        var page = activeDetailPage();
        var itemId = itemIdFromHash();
        if (!page || !itemId) { return; }
        var misc = page.querySelector('.itemMiscInfo-primary');
        if (!misc) { scheduleRender(250); return; }
        if (page.dataset.laurelsItem === itemId && page.querySelector('.laurels-strip, .laurels-awards')) { return; }
        page.dataset.laurelsItem = itemId;
        currentItemId = itemId;
        clear(page);

        loadSettings().then(function () {
            return getJson('Laurels/Items/' + itemId);
        }).then(function (data) {
            if (currentItemId !== itemId || !data) { return; }
            injectStyle();
            var anchor = misc.parentNode; // .infoWrapper
            if (data.found) {
                replaceIcons(page, data);
                if (settings.showRatingsPanel) {
                    var strip = buildStrip(data);
                    if (strip) { anchor.insertBefore(strip, misc.nextSibling); }
                }
            }
            if (settings.showAwardBadges) {
                getJson('Laurels/Awards/' + itemId).then(function (awards) {
                    if (currentItemId !== itemId) { return; }
                    var el = buildAwards(awards);
                    if (el) { anchor.insertBefore(el, misc.nextSibling); }
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
        var observer = new MutationObserver(function () {
            var page = activeDetailPage();
            if (page && itemIdFromHash() && !page.querySelector('.laurels-strip')) { scheduleRender(150); }
        });
        observer.observe(document.body, { childList: true, subtree: true });
        scheduleRender(500);
        log('ready');
    }

    if (document.readyState === 'loading') { document.addEventListener('DOMContentLoaded', init, { once: true }); } else { init(); }
}());
