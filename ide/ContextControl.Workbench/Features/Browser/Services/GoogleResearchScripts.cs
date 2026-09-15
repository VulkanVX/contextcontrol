namespace ContextControl.Workbench.Services;

/// <summary>Read-only DOM extraction. Scripts never click, submit, or execute model output.</summary>
public static class GoogleResearchScripts
{
    public const string SearchResults = """
        (() => {
          const results = [], seen = new Set();
          const interfaceLabel = value => /^(translate (this|the) page|translated by google|isversti\b|tulkot\b|tolgi see leht|перевести (эту )?страницу|more results|view all results)/i.test(value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').trim());
          const needsConsent = !!document.querySelector('form[action*="consent.google"], [role="dialog"] form[action*="consent"], [aria-modal="true"] form');
          if (needsConsent) return { url: location.href, needsConsent: true, results };
          for (const h of document.querySelectorAll('h3')) {
            const a = h.closest('a');
            if (!a || !h.innerText.trim() || interfaceLabel(h.innerText)) continue;
            let url;
            try {
              url = new URL(a.href);
              if (url.hostname === 'www.google.com' && url.pathname === '/url') {
                const target = url.searchParams.get('q') || url.searchParams.get('url') || '';
                if (/^https?:\/\//.test(target)) url = new URL(target);
              }
            } catch { continue; }
            const googleRedirect = url.hostname === 'www.google.com' && ['/goto','/url'].includes(url.pathname);
            if (!['http:', 'https:'].includes(url.protocol) || (/^(.+\.)?google\.[a-z.]+$/.test(url.hostname) && !googleRedirect) || seen.has(url.href)) continue;
            seen.add(url.href);
            let block = h.closest('.MjjYud, .g, [data-sokoban-container]') || a.parentElement;
            const resultBlock = block;
            for (let i = 0; i < 3 && block && block.innerText.length < 120; i++) block = block.parentElement;
            const photo = Array.from(resultBlock?.querySelectorAll('img') || []).find(img => {
              const rect = img.getBoundingClientRect();
              return rect.width >= 80 && rect.height >= 50 && getComputedStyle(img).visibility !== 'hidden';
            });
            const imageUrl = photo?.currentSrc || photo?.src || '';
            const snippet = (block?.innerText || '').split('\n').filter(line => !interfaceLabel(line)).join('\n');
            results.push({ title: h.innerText.trim().slice(0,160), url: url.href, snippet: snippet.trim().slice(0,700),
              imageUrl: imageUrl.length <= 524288 ? imageUrl : '' });
            if (results.length === 6) break;
          }
          return { url: location.href, results };
        })()
        """;

    public const string PageText = """
        (() => {
          const root = document.querySelector('article') || document.querySelector('[role="main"], main') || document.body;
          if (!root) return { url: location.href, text: '' };
          const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
          const parts = []; let length = 0, node;
          while ((node = walker.nextNode()) && length < 24000) {
            const p = node.parentElement;
            if (!p || p.closest('script,style,noscript,nav,footer,header,form,button,svg,[aria-hidden="true"],[hidden]')) continue;
            if (!p.getClientRects().length) continue;
            const style = getComputedStyle(p);
            if (style.visibility === 'hidden' || style.display === 'none') continue;
            const text = node.textContent.replace(/\s+/g, ' ').trim();
            if (text) { parts.push(text); length += text.length + 1; }
          }
          const gate = document.querySelector('[role="dialog"][aria-modal="true"],dialog[open]');
          const images = [], seen = new Set();
          const headings = Array.from(root.querySelectorAll('h2,h3,h4'));
          const sectionFor = node => {
            let section = '';
            for (const h of headings) if (h.compareDocumentPosition(node) & Node.DOCUMENT_POSITION_FOLLOWING) section = h.innerText;
            return section.trim().slice(0,180);
          };
          const add = (value, caption = '', section = '', kind = 'Article image') => {
            if (!value || value.length > 524288) return;
            try {
              const image = new URL(value, document.baseURI);
              if (seen.has(image.href) || !(['http:', 'https:'].includes(image.protocol) || /^data:image\/(png|jpeg|webp|gif);base64,/i.test(value))) return;
              seen.add(image.href);
              if (/\b(logo)\b/i.test(caption) || /(?:^|[/_.-])logo(?:[/_.-]|$)/i.test(image.pathname)) kind = 'Logo';
              images.push({url:image.href,caption:caption.trim().slice(0,180),section,kind});
            } catch { }
          };
          for (const node of root.querySelectorAll('img,video[poster]')) {
            const rect = node.getBoundingClientRect();
            const width = node.naturalWidth || Number(node.getAttribute('width')) || rect.width;
            const height = node.naturalHeight || Number(node.getAttribute('height')) || rect.height;
            // Some publishers place real article illustrations inside <aside>.
            // Exclude identified sidebar/promotional regions, not that semantic tag alone.
            if (width < 180 || height < 70 || node.closest('nav,header,footer,[role="complementary"],[role="dialog"],form,button,[hidden],[aria-hidden="true"]') || getComputedStyle(node).visibility === 'hidden') continue;
            let promotional = false;
            for (let ancestor = node; ancestor && ancestor !== root; ancestor = ancestor.parentElement)
              if (/(?:^|[\s_-])(?:ads?|advertisement|sponsored|newsletter|subscribe|subscription|premium|sidebar|related|recommended)(?:[\s_-]|$)/i.test((ancestor.id || '') + ' ' + (ancestor.className || ''))) { promotional = true; break; }
            if (promotional) continue;
            const caption = node.closest('figure')?.querySelector('figcaption')?.innerText || node.getAttribute('alt') || node.getAttribute('title') || '';
            add(node.tagName === 'VIDEO' ? node.poster : node.currentSrc || node.getAttribute('data-src') || node.src, caption, sectionFor(node), node.tagName === 'VIDEO' ? 'Video preview' : 'Article image');
            if (images.length === 20) break;
          }
          for (const selector of ['meta[property="og:image:secure_url"]','meta[property="og:image"]','meta[name="twitter:image"],meta[property="twitter:image"]'])
            add(document.querySelector(selector)?.content);
          const imageUrl = document.querySelector('meta[property="og:image"]')?.content || images[0]?.url || '';
          return { url: location.href, title: document.title, text: parts.join('\n').slice(0,24000),
            imageUrl, images,
            heading: document.querySelector('h1')?.innerText?.slice(0,500) || '',
            gateText: gate?.innerText?.slice(0,1600) || '', hasArticle: !!document.querySelector('article,shreddit-post'),
            hasPasswordField: Array.from(document.querySelectorAll('input[type="password"]')).some(e => e.getClientRects().length && getComputedStyle(e).visibility !== 'hidden'),
            needsConsent: !!gate && /cookies|slapuk|sīkdat|küpsis|файлы cookie/i.test(gate.innerText) };
        })()
        """;
}
