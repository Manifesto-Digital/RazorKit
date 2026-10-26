// RazorKit explorer page script. A classic script, not a module: the page loads it at the end of
// <body>, so the elements it binds to exist and its functions stay global as they were inline.
// Server values come from the non-executing JSON block in the page, never from markup in this file.
const razorKitData = JSON.parse(document.getElementById('razorkit-data').textContent);

// Not read by the script today; carried over from the page so the data block holds everything the
// page script was given.
const propertyMetadata = razorKitData.properties;

function resetForm() {
    const form = document.getElementById('component-form');
    if (form) {
        form.reset();
        updateIframePreview();
        updateComponentCode();
    }
}

function openComponentInNewWindow() {
    const iframe = document.getElementById('preview-iframe');
    const openBtn = document.querySelector('.razorkit-open-window-btn');

    if (!iframe || !iframe.src) {
        alert('No component preview available to open');
        return;
    }

    openBtn.disabled = true;

    const newWindow = window.open(iframe.src, '_blank', 'width=1200,height=800,scrollbars=yes,resizable=yes');

    if (!newWindow) {
        alert('Popup blocked. Please allow popups for this site to open components in new windows.');
    }

    setTimeout(() => {
        openBtn.disabled = false;
    }, 1000);
}

function toggleSidebarLayout(layout) {
    const body = document.body;
    const layoutButtons = document.querySelectorAll('.razorkit-layout-btn');
    const showControlsBtn = document.querySelector('.razorkit-show-controls-btn');

    body.classList.remove('layout-bottom', 'layout-hidden');
    layoutButtons.forEach(btn => btn.classList.remove('active'));

    if (layout === 'bottom') {
        body.classList.add('layout-bottom');
        const bottomBtn = document.getElementById('layout-bottom');
        if (bottomBtn) bottomBtn.classList.add('active');
        showControlsBtn.style.display = 'none';
    } else if (layout === 'hidden') {
        body.classList.add('layout-hidden');
        const hiddenBtn = document.getElementById('layout-hidden');
        if (hiddenBtn) hiddenBtn.classList.add('active');
        showControlsBtn.style.display = 'flex';
    } else {
        const sideBtn = document.getElementById('layout-side');
        if (sideBtn) sideBtn.classList.add('active');
        showControlsBtn.style.display = 'none';
    }

    localStorage.setItem('razorkit-sidebar-layout', layout);
    setTimeout(updateViewportInfo, 100);
}

function resetToStory() {
    window.location.reload();
}

document.addEventListener('change', function(evt) {
    if (evt.target.closest('#component-form')) {
        updateIframePreview();
        updateComponentCode();
    }
});

document.addEventListener('keyup', function(evt) {
    if (evt.target.closest('#component-form') && evt.target.type === 'text') {
        clearTimeout(window.updateTimeout);
        window.updateTimeout = setTimeout(function() {
            updateIframePreview();
            updateComponentCode();
        }, 300);
    }
});

function toggleStories(componentName) {
    const storiesList = document.getElementById('stories-' + componentName);
    if (storiesList) {
        storiesList.classList.toggle('expanded');
    }
}

function updateComponentCode() {
    const form = document.getElementById('component-form');
    if (!form) return;

    const formData = new FormData(form);
    const props = {};

    for (let [key, value] of formData.entries()) {
        if (key !== 'componentName') {
            if (value === 'true' || value === 'false') {
                props[key] = value === 'true';
            } else if (!isNaN(value) && value !== '') {
                props[key] = parseFloat(value);
            } else {
                props[key] = value;
            }
        }
    }

    const codeDiv = document.getElementById('component-code');
    if (codeDiv) {
        // Lines are built with textContent: prop values come from the Controls panel and must not be parsed as markup.
        const addLine = function (parts, indent) {
            const line = document.createElement('div');
            if (indent) {
                line.style.marginLeft = '1rem';
            }
            parts.forEach(function (part) {
                const span = document.createElement('span');
                span.style.color = part[1];
                span.textContent = part[0];
                line.appendChild(span);
            });
            return line;
        };

        const lines = [
            addLine([['@await Html.PartialAsync("' + razorKitData.componentPath + '", new ' + razorKitData.componentName + 'Props', '#059669']], false),
            addLine([['{', '#dc2626']], false)
        ];

        Object.entries(props).forEach(([key, value]) => {
            const valueStr = typeof value === 'string' ? `"${value}"` : value.toString();
            lines.push(addLine([[key, '#7c2d12'], [' = ', '#374151'], [valueStr, '#0369a1'], [',', '#374151']], true));
        });

        lines.push(addLine([['})', '#dc2626']], false));
        codeDiv.replaceChildren(...lines);
    }

    clearA11yResults();
}

function toggleA11yPanel() {
    const content = document.getElementById('a11y-content');
    const icon = document.getElementById('a11y-toggle-icon');

    if (content && icon) {
        content.classList.toggle('expanded');
        icon.textContent = content.classList.contains('expanded') ? '▼' : '▶';
    }
}

function runA11yTest() {
    const button = document.getElementById('a11y-run-button');
    const summary = document.getElementById('a11y-summary');
    const issues = document.getElementById('a11y-issues');

    if (!button || !summary || !issues) return;

    button.disabled = true;
    button.textContent = 'Running Test...';
    summary.innerHTML = '';
    issues.innerHTML = '';

    const iframe = document.getElementById('preview-iframe');
    if (!iframe) {
        button.disabled = false;
        button.textContent = 'Run Accessibility Test';
        summary.innerHTML = '<span class="razorkit-a11y-badge violations">No iframe found</span>';
        return;
    }

    function runTestOnIframe() {
        try {
            if (!iframe.contentDocument || !iframe.contentWindow) {
                throw new Error('Cannot access iframe content');
            }

            iframe.contentWindow.axe.run(iframe.contentDocument)
                .then(function (results) {
                    displayA11yResults(results);
                })
                .catch(function (error) {
                    const badge = document.createElement('span');
                    badge.className = 'razorkit-a11y-badge violations';
                    badge.textContent = 'Error running test: ' + error.message;
                    summary.replaceChildren(badge);
                })
                .finally(function () {
                    button.disabled = false;
                    button.textContent = 'Run Accessibility Test';
                });
        } catch (error) {
            summary.innerHTML = '<span class="razorkit-a11y-badge violations">Cannot access iframe content</span>';
            button.disabled = false;
            button.textContent = 'Run Accessibility Test';
        }
    }

    if (iframe.contentDocument && iframe.contentDocument.readyState === 'complete') {
        runTestOnIframe();
    } else {
        iframe.addEventListener('load', runTestOnIframe, { once: true });
    }
}

function displayA11yResults(results) {
    const summary = document.getElementById('a11y-summary');
    const issues = document.getElementById('a11y-issues');

    if (!summary || !issues) return;

    const violations = results.violations.length;
    const passes = results.passes.length;
    const incomplete = results.incomplete.length;

    // Create summary badges
    let summaryHTML = '';

    if (violations === 0) {
        summaryHTML += '<span class="razorkit-a11y-badge pass">✓ ' + passes + ' Passed</span>';
    } else {
        summaryHTML += '<span class="razorkit-a11y-badge violations">⚠ ' + violations + ' Violations</span>';
    }

    if (passes > 0) {
        summaryHTML += '<span class="razorkit-a11y-badge pass">✓ ' + passes + ' Passed</span>';
    }

    if (incomplete > 0) {
        summaryHTML += '<span class="razorkit-a11y-badge incomplete">⚠ ' + incomplete + ' Incomplete</span>';
    }

    summary.innerHTML = summaryHTML;

    // Display violations. Every axe-supplied string is inserted as text: axe messages can echo
    // attribute values from the previewed component, so none of it may be parsed as markup here.
    function el(tag, className, style, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (style) node.setAttribute('style', style);
        if (text !== undefined) node.textContent = text;
        return node;
    }

    // Only https: documentation links are rendered; anything else (javascript:, data:, relative) is dropped.
    function safeHttpsUrl(value) {
        try {
            const url = new URL(String(value));
            return url.protocol === 'https:' ? url.href : null;
        } catch (e) {
            return null;
        }
    }

    issues.textContent = '';

    results.violations.forEach(function(violation, index) {
        const impact = violation.impact || 'moderate';
        const impactClass = String(impact).replace(/[^a-z]/gi, '').toLowerCase();

        const elementsList = document.createElement('div');
        violation.nodes.forEach(function(node, nodeIndex) {
            const target = (node.target || []).join(', ') || 'Unknown';
            const html = node.html || 'HTML not available';
            const failureSummary = node.failureSummary || 'No summary available';

            const element = el('div', 'razorkit-a11y-element', 'margin-bottom: 1rem; padding: 0.75rem; background: #f9f9f9; border-radius: 0.375rem; border-left: 3px solid #ef4444;');

            const targetRow = el('div', null, 'margin-bottom: 0.5rem;');
            targetRow.appendChild(el('strong', null, 'color: #374151;', 'Element ' + (nodeIndex + 1) + ':'));
            targetRow.appendChild(document.createTextNode(' '));
            targetRow.appendChild(el('code', null, 'background: #e5e7eb; padding: 0.25rem 0.5rem; border-radius: 0.25rem; font-size: 0.75rem; color: #1f2937;', target));
            element.appendChild(targetRow);

            const htmlRow = el('div', null, 'margin-bottom: 0.5rem;');
            htmlRow.appendChild(el('strong', null, 'color: #374151;', 'HTML:'));
            const pre = el('pre', null, 'background: #ffffff; padding: 0.5rem; border-radius: 0.25rem; font-size: 0.75rem; white-space: pre-wrap; word-break: break-all; margin: 0.25rem 0; border: 1px solid #d1d5db; max-height: 100px; overflow-y: auto;');
            pre.appendChild(el('code', null, null, html));
            htmlRow.appendChild(pre);
            element.appendChild(htmlRow);

            const issueRow = document.createElement('div');
            issueRow.appendChild(el('strong', null, 'color: #374151;', 'Issue:'));
            issueRow.appendChild(el('div', null, 'color: #6b7280; font-size: 0.875rem; margin-top: 0.25rem;', failureSummary));
            element.appendChild(issueRow);

            elementsList.appendChild(element);
        });

        const issue = el('div', 'razorkit-a11y-issue');

        const header = el('div', 'razorkit-a11y-issue-header');
        header.addEventListener('click', function() { toggleA11yIssue(index); });
        header.appendChild(el('div', 'razorkit-a11y-issue-title', null, String(violation.description)));
        header.appendChild(el('div', 'razorkit-a11y-issue-impact ' + impactClass, null, String(impact).toUpperCase()));
        issue.appendChild(header);

        const details = el('div', 'razorkit-a11y-issue-details');
        details.id = 'a11y-issue-' + index;
        const body = el('div', 'razorkit-a11y-issue-body');

        const meta = el('div', null, 'margin-bottom: 1rem;');
        const helpP = document.createElement('p');
        helpP.appendChild(el('strong', null, null, 'Help:'));
        helpP.appendChild(document.createTextNode(' ' + String(violation.help)));
        meta.appendChild(helpP);
        const ruleP = document.createElement('p');
        ruleP.appendChild(el('strong', null, null, 'Rule:'));
        ruleP.appendChild(document.createTextNode(' ' + String(violation.id)));
        meta.appendChild(ruleP);
        const countP = document.createElement('p');
        countP.appendChild(el('strong', null, null, 'Elements affected:'));
        countP.appendChild(document.createTextNode(' ' + violation.nodes.length));
        meta.appendChild(countP);

        const helpHref = violation.helpUrl ? safeHttpsUrl(violation.helpUrl) : null;
        if (helpHref) {
            const linkP = document.createElement('p');
            const link = el('a', null, 'color: #3b82f6;', 'Learn more →');
            link.href = helpHref;
            link.target = '_blank';
            link.rel = 'noopener noreferrer';
            linkP.appendChild(link);
            meta.appendChild(linkP);
        }
        body.appendChild(meta);

        const affected = document.createElement('div');
        affected.appendChild(el('h5', null, 'margin: 0 0 0.5rem 0; font-size: 0.875rem; font-weight: 600; color: #374151;', 'Affected Elements:'));
        affected.appendChild(elementsList);
        body.appendChild(affected);

        details.appendChild(body);
        issue.appendChild(details);
        issues.appendChild(issue);
    });

    // Auto-expand the accessibility panel if there are violations
    if (violations > 0) {
        const content = document.getElementById('a11y-content');
        const icon = document.getElementById('a11y-toggle-icon');
        if (content && !content.classList.contains('expanded')) {
            content.classList.add('expanded');
            if (icon) icon.textContent = '▼';
        }
    }
}

function toggleA11yIssue(index) {
    const details = document.getElementById('a11y-issue-' + index);
    if (details) {
        details.classList.toggle('expanded');
    }
}

function clearA11yResults() {
    const summary = document.getElementById('a11y-summary');
    const issues = document.getElementById('a11y-issues');

    if (summary) summary.innerHTML = '';
    if (issues) issues.innerHTML = '';
}

// Viewport Management Functions
function changeViewport() {
    const selector = document.getElementById('viewport-selector');
    const preview = document.querySelector('.razorkit-preview');

    if (!selector || !preview) return;

    const selectedViewport = selector.value;

    // Remove all viewport classes
    preview.classList.remove('viewport-mobile', 'viewport-tablet', 'viewport-laptop', 'viewport-desktop', 'viewport-large', 'viewport-responsive');

    // Add the selected viewport class
    preview.classList.add('viewport-' + selectedViewport);

    // Force iframe to refresh with new viewport dimensions
    setTimeout(function() {
        const iframe = document.getElementById('preview-iframe');
        if (iframe) {
            // Force a reload by temporarily changing and restoring the src
            const currentSrc = iframe.src;
            iframe.src = 'about:blank';
            setTimeout(function() {
                iframe.src = currentSrc;
            }, 10);
        }

        // Update viewport info display after iframe loads
        setTimeout(updateViewportInfo, 100);
    }, 10);

    // Store preference in localStorage
    localStorage.setItem('razorkit-viewport', selectedViewport);
}

function updateViewportInfo() {
    const iframe = document.getElementById('preview-iframe');
    const viewportInfo = document.getElementById('viewport-info');
    const sizeSpan = document.getElementById('viewport-size');

    if (!iframe || !viewportInfo || !sizeSpan) return;

    function measureAndUpdate() {
        const rect = iframe.getBoundingClientRect();
        const width = Math.round(rect.width);
        const height = Math.round(rect.height);

        const selector = document.getElementById('viewport-selector');
        const selectedOption = selector?.options[selector.selectedIndex];
        const viewportName = selectedOption?.text.split(' (')[0] || 'Unknown';

        sizeSpan.textContent = `${viewportName} (${width} × ${height}px)`;
    }

    if (iframe.contentDocument && iframe.contentDocument.readyState === 'complete') {
        measureAndUpdate();
    } else {
        iframe.addEventListener('load', measureAndUpdate, { once: true });
        setTimeout(measureAndUpdate, 200);
    }
}

function updateIframePreview() {
    const form = document.getElementById('component-form');
    const iframe = document.getElementById('preview-iframe');

    if (!form || !iframe) return;

    const formData = new FormData(form);
    const componentName = formData.get('componentName');
    const storyName = formData.get('storyName') || 'default';

    // Build props object from form data
    const props = {};

    for (let [key, value] of formData.entries()) {
        if (key !== 'componentName' && key !== 'storyName') {
            // Handle checkboxes properly
            const inputElement = form.querySelector(`[name="${key}"]`);
            if (inputElement && inputElement.type === 'checkbox') {
                props[key] = inputElement.checked;
            } else if (inputElement && inputElement.tagName === 'TEXTAREA') {
                // Try to parse JSON for complex types
                try {
                    const trimmedValue = value.trim();
                    if (trimmedValue) {
                        props[key] = JSON.parse(trimmedValue);
                    } else {
                        props[key] = null;
                    }
                } catch (e) {
                    console.error(`Failed to parse JSON for ${key}:`, e);
                    // Keep the string value if JSON parsing fails
                    props[key] = value;
                }
            } else {
                props[key] = value;
            }
        }
    }

    // POST the data to update the iframe
    postToIframe(iframe, `/razorkit-preview/preview/${componentName}/${storyName}`, props);
}

function postToIframe(iframe, url, data) {
    try {
        // Create a temporary form to POST to the iframe
        const form = document.createElement('form');
        form.method = 'POST';
        form.action = url;
        form.target = iframe.name || 'preview-iframe';
        form.style.display = 'none';

        // Add props as JSON
        const input = document.createElement('input');
        input.type = 'hidden';
        input.name = 'propsJson';
        input.value = JSON.stringify(data);
        form.appendChild(input);

        // The server rejects a render POST without the token the page was issued.
        const token = document.querySelector('meta[name="razorkit-token"]');
        const tokenInput = document.createElement('input');
        tokenInput.type = 'hidden';
        tokenInput.name = razorKitData.tokenField;
        tokenInput.value = token ? token.content : '';
        form.appendChild(tokenInput);

        document.body.appendChild(form);
        form.submit();
        document.body.removeChild(form);
    } catch (e) {
        console.error('Error posting to iframe:', e);
    }
}

function initializeViewport() {
    const savedViewport = localStorage.getItem('razorkit-viewport');
    const selector = document.getElementById('viewport-selector');

    if (savedViewport && selector) {
        selector.value = savedViewport;
        const preview = document.querySelector('.razorkit-preview');
        if (preview) {
            preview.classList.add('viewport-' + savedViewport);
        }
    }

    setTimeout(updateViewportInfo, 100);

    let resizeTimeout;
    window.addEventListener('resize', function() {
        clearTimeout(resizeTimeout);
        resizeTimeout = setTimeout(updateViewportInfo, 100);
    });

    const observer = new MutationObserver(function() {
        setTimeout(updateViewportInfo, 100);
    });

    const previewContainer = document.getElementById('component-preview');
    if (previewContainer) {
        observer.observe(previewContainer, { childList: true, subtree: true });
    }
}

// Initialize with POST if there's a form
function initializePreview() {
    const form = document.getElementById('component-form');
    if (form) {
        // Give iframe time to load, then POST the initial data
        const iframe = document.getElementById('preview-iframe');
        if (iframe) {
            iframe.addEventListener('load', function() {
                updateIframePreview();
            }, { once: true });
        }
    }
}

function initializeIframeWithDefaults() {
    const form = document.getElementById('component-form');
    const iframe = document.getElementById('preview-iframe');

    if (form && iframe) {
        // Only update component code on initial load
        // Component renders correctly via GET request
        updateComponentCode();
    }
}

// The page markup carries no inline handlers. Controls name their action in data-razorkit-action
// and one delegated listener dispatches it, which also covers the generated sidebar entries.
document.addEventListener('click', function(evt) {
    const control = evt.target.closest('[data-razorkit-action]');
    if (!control) return;
    // A click on a child span or svg inside a disabled button can still bubble to this listener
    // in some browsers; the old inline handlers never fired on a disabled button.
    if (control.disabled || control.getAttribute('aria-disabled') === 'true') return;

    switch (control.dataset.razorkitAction) {
        case 'layout':
            toggleSidebarLayout(control.dataset.razorkitLayout);
            break;
        case 'open-window':
            openComponentInNewWindow();
            break;
        case 'toggle-stories':
            toggleStories(control.dataset.razorkitComponent);
            break;
        case 'reset-form':
            resetForm();
            break;
        case 'reset-story':
            resetToStory();
            break;
        case 'toggle-a11y':
            toggleA11yPanel();
            break;
        case 'run-a11y':
            runA11yTest();
            break;
    }
});

document.addEventListener('change', function(evt) {
    if (evt.target.id === 'viewport-selector') {
        changeViewport();
    }
});

document.addEventListener('DOMContentLoaded', function() {
    initializeViewport();

    const savedLayout = localStorage.getItem('razorkit-sidebar-layout') || 'side';
    toggleSidebarLayout(savedLayout);

    setTimeout(function() {
        initializeIframeWithDefaults();
    }, 100);

    const previewContainer = document.getElementById('component-preview');
    if (previewContainer && previewContainer.children.length > 0) {
        setTimeout(runA11yTest, 1000);
    }
});
