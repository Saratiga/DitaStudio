using System.Globalization;
using System.Text;

namespace DitaStudio.Core.Publishing;

/// <summary>Лист бумаги: размеры и поля, мм (как в «Параметры страницы»).</summary>
public sealed record PageGeometry(double WidthMm, double HeightMm, double TopMm, double RightMm, double BottomMm, double LeftMm)
{
    public static readonly PageGeometry A4 = new(210, 297, 20, 20, 20, 20);

    /// <summary>Лист из параметров страницы проекта: бумага, ориентация, поля; не заданное — A4 с полями 20 мм.</summary>
    public static PageGeometry For(DocxLayout layout)
    {
        var (width, height) = layout.PaperMm() ?? (A4.WidthMm, A4.HeightMm);
        if (layout.Landscape && width < height)
        {
            (width, height) = (height, width);
        }

        return new PageGeometry(width, height, layout.MarginTopMm ?? 20, layout.MarginRightMm ?? 20, layout.MarginBottomMm ?? 20, layout.MarginLeftMm ?? 20);
    }

    public double ContentWidthMm => Math.Max(20, WidthMm - LeftMm - RightMm);

    public double ContentHeightMm => Math.Max(20, HeightMm - TopMm - BottomMm);
}

/// <summary>
/// «Живой» постраничный предпросмотр: страница с листами заданного размера (бумага, ориентация, поля из «Параметров страницы»),
/// на которые текст раскладывается в самом браузере скриптом — без печати в PDF, поэтому пересчёт занимает доли секунды и
/// выполняется при каждой правке (<c>ditaSetContent</c> подменяет текст и раскладывает заново, не перезагружая страницу).
/// Разрывы — между блоками (абзац, строка таблицы, пункт списка не режется), заголовок остаётся с следующим блоком; вложенность
/// (списки, разделы, таблицы) сохраняется на каждой странице; блок с разрывом страницы перед собой (page-break-before) начинает новый лист.
/// Сноски — как в Word: внизу того листа, где на них ссылаются, под короткой чертой, с надстрочным номером. Текст оформлен как документ
/// Word (<see cref="Assets.WordPreviewCss"/>: Calibri 11 пт, чёрные заголовки), колонтитулы — текст из настроек с полями {page}, {pages},
/// {title}, {date}. Результат близок к печати в PDF и DOCX, но не пиксель-в-пиксель: точные разрывы даёт режим PDF.
/// </summary>
public static class PagedPreview
{
    private static string Mm(double value) => value.ToString("0.###", CultureInfo.InvariantCulture) + "mm";

    /// <summary>Страница предпросмотра: стили публикации, листы и скрипт раскладки; <paramref name="initialBodyHtml"/> — первый текст.</summary>
    public static string Build(PageGeometry page, string css, string title, string headerText, string footerText, string initialBodyHtml)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"ru\">\n<head>\n<meta charset=\"utf-8\" />\n<title>")
          .Append(System.Net.WebUtility.HtmlEncode(title)).Append("</title>\n");
        sb.Append("<style>\n").Append(css).Append("\n</style>\n");
        // Вид страницы Word: шрифт, интервалы и заголовки документа Word поверх стилей публикации.
        sb.Append("<style id=\"word-css\">\n").Append(Assets.WordPreviewCss)
          .Append(".sheet main p{margin:0 0 8pt;}\n.sheet main li>p{margin:0;}\n.sheet main h1{margin-top:0;}\n")
          .Append(".sheet main .related-links,.sheet main .pager,.sheet main .breadcrumbs{display:none;}\n")
          .Append("</style>\n");
        sb.Append("<style id=\"paged-css\">\n")
          .Append("html,body{margin:0;padding:0;background:#8a8f98;}\n")
          .Append("#pages{display:flex;flex-direction:column;align-items:center;gap:18px;padding:18px 0 40px;}\n")
          .Append(".sheet{position:relative;background:#fff;color:#1b1f23;box-sizing:border-box;flex:none;overflow:hidden;")
          .Append("width:").Append(Mm(page.WidthMm)).Append(";height:").Append(Mm(page.HeightMm)).Append(";box-shadow:0 2px 10px rgba(0,0,0,.45);}\n")
          .Append(".sheet .content{position:absolute;overflow:hidden;display:flex;flex-direction:column;left:").Append(Mm(page.LeftMm)).Append(";top:").Append(Mm(page.TopMm))
          .Append(";width:").Append(Mm(page.ContentWidthMm)).Append(";height:").Append(Mm(page.ContentHeightMm)).Append(";}\n")
          .Append(".sheet .content>main{display:block;flex:none;max-width:none;margin:0;padding:0;}\n")
          // Сноски листа — внизу области текста, как в Word: черта в треть ширины, мелкий шрифт, номер надстрочным знаком.
          .Append(".sheet .fnarea{margin-top:auto;flex:none;padding-top:6pt;font:9pt/1.2 Calibri,'Segoe UI',Arial,sans-serif;color:#000;}\n")
          .Append(".sheet .fnarea hr{width:33%;margin:0 0 3pt 0;border:0;border-top:.75pt solid #000;}\n")
          .Append(".sheet .fnarea ol{list-style:none;margin:0;padding:0;}\n.sheet .fnarea li{margin:0 0 2pt;}\n")
          .Append(".sheet .fnarea .fn-num{vertical-align:super;font-size:.75em;margin-right:2pt;}\n")
          .Append(".sheet a.fn-ref{vertical-align:super;font-size:.7em;line-height:0;color:inherit;text-decoration:none;}\n")
          .Append(".sheet .content>main>:first-child,.sheet .content>main>:first-child>:first-child{margin-top:0;}\n")
          .Append(".sheet .head,.sheet .foot{position:absolute;left:").Append(Mm(page.LeftMm)).Append(";right:").Append(Mm(page.RightMm))
          .Append(";font:9px 'Segoe UI',Arial,sans-serif;color:#666;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}\n")
          .Append(".sheet .head{top:").Append(Mm(Math.Max(3, page.TopMm / 3))).Append(";}\n")
          .Append(".sheet .foot{bottom:").Append(Mm(Math.Max(3, page.BottomMm / 3))).Append(";text-align:center;}\n")
          .Append("#src{position:absolute;left:-100000px;top:0;visibility:hidden;width:").Append(Mm(page.ContentWidthMm)).Append(";}\n")
          .Append("#src>main,.sheet main{max-width:none;}\n")
          .Append("</style>\n</head>\n<body>\n");
        sb.Append("<div id=\"src\"><main></main></div>\n<div id=\"pages\"></div>\n");
        sb.Append("<script>\nvar CONFIG = ").Append(Config(title, headerText, footerText)).Append(";\nvar INITIAL = ")
          .Append(JsString(initialBodyHtml)).Append(";\n").Append(Script).Append("\n</script>\n</body>\n</html>\n");
        return sb.ToString();
    }

    private static string Config(string title, string header, string footer) =>
        "{title:" + JsString(title) + ",header:" + JsString(header) + ",footer:" + JsString(footer) +
        ",date:" + JsString(DateTime.Now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)) + "}";

    /// <summary>Строка как литерал JavaScript (для вставки в скрипт или в вызов <c>ditaSetContent</c>).</summary>
    public static string JsString(string? value)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in value ?? string.Empty)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case (char)0x2028: sb.Append("\\u2028"); break;
                case (char)0x2029: sb.Append("\\u2029"); break;
                case '<': sb.Append("\\u003c"); break; // </script> внутри строки не закроет тег
                default: sb.Append(c); break;
            }
        }

        return sb.Append('"').ToString();
    }

    /// <summary>Вызов подмены текста на уже загруженной странице предпросмотра.</summary>
    public static string SetContentCall(string bodyHtml) => "if(window.ditaSetContent){ditaSetContent(" + JsString(bodyHtml) + ");}";

    // Раскладка: блоки копируются на листы по одному; контейнеры (div, списки, таблицы…) разбираются по детям, их «оболочки»
    // повторяются на следующей странице. Всё, что не помещается целиком, переносится; единственный блок больше листа остаётся
    // на своём листе (обрезается его полем). После раскладки заполняются колонтитулы и атрибут data-pages.
    private const string Script = """
        (function () {
          var pagesEl = document.getElementById('pages');
          var srcMain = document.querySelector('#src > main');
          var CONTAINERS = /^(DIV|SECTION|ARTICLE|ASIDE|NAV|UL|OL|DL|BLOCKQUOTE|FIGURE|TABLE|THEAD|TBODY|TFOOT)$/;
          var HEADINGS = /^H[1-6]$/;
          var sheet, content, main, stack, placed, sheets, footnotes;

          // Сноски публикации (список в конце топика) уходят из текста: каждая встаёт внизу листа со ссылкой на неё.
          function extractFootnotes() {
            var blocks = srcMain.querySelectorAll('.footnotes');
            if (!blocks.length && footnotes) { return; } // повторная раскладка того же текста (ditaPaginate)
            footnotes = {};
            for (var b = 0; b < blocks.length; b++) {
              var items = blocks[b].querySelectorAll('li[id]');
              for (var i = 0; i < items.length; i++) {
                var li = items[i].cloneNode(true);
                var back = li.querySelector('a[href^="#fnref"]');
                if (back) { back.parentNode.removeChild(back); }
                footnotes[items[i].id] = li.innerHTML.replace(/\s+$/, '');
              }
              blocks[b].parentNode.removeChild(blocks[b]);
            }
          }

          function footnoteList() {
            var ol = content.querySelector('.fnarea ol');
            if (!ol) {
              var area = document.createElement('div');
              area.className = 'fnarea';
              area.innerHTML = '<hr><ol></ol>';
              content.appendChild(area);
              ol = area.querySelector('ol');
            }
            return ol;
          }

          // Ссылки на сноски в только что поставленном блоке: номер без скобок, текст сноски — внизу этого листа.
          function addFootnotes(el) {
            var added = [];
            if (el.nodeType !== 1) { return added; }
            var refs = el.matches('a.fn-ref') ? [el] : Array.prototype.slice.call(el.querySelectorAll('a.fn-ref'));
            for (var i = 0; i < refs.length; i++) {
              var a = refs[i];
              var id = (a.getAttribute('href') || '').replace(/^#/, '');
              var marker = a.textContent.replace(/^\[|\]$/g, '');
              a.textContent = marker;
              if (!(id in footnotes)) { continue; }
              var li = document.createElement('li');
              var num = document.createElement('span');
              num.className = 'fn-num';
              num.textContent = marker;
              li.appendChild(num);
              li.insertAdjacentHTML('beforeend', footnotes[id]);
              footnoteList().appendChild(li);
              added.push(li);
            }
            return added;
          }

          function removeFootnotes(added) {
            for (var i = 0; i < added.length; i++) {
              var ol = added[i].parentNode;
              ol.removeChild(added[i]);
              if (!ol.children.length) { var area = ol.parentNode; area.parentNode.removeChild(area); }
            }
          }

          function breaksBefore(el) {
            if (el.nodeType !== 1) { return false; }
            if (el.classList.contains('page-break-before')) { return true; }
            var s = window.getComputedStyle(el);
            return s.breakBefore === 'page' || s.pageBreakBefore === 'always';
          }

          function newSheetElement() {
            var s = document.createElement('div');
            s.className = 'sheet';
            s.innerHTML = '<div class="head"></div><div class="content"><main></main></div><div class="foot"></div>';
            pagesEl.appendChild(s);
            sheets.push(s);
            return s;
          }

          function shallow(el) {
            var w = el.cloneNode(false);
            w.removeAttribute('id');
            if (el.tagName === 'TABLE') {
              var cg = el.querySelector(':scope > colgroup');
              if (cg) { w.appendChild(cg.cloneNode(true)); }
            }
            return w;
          }

          function host() { return stack.length ? stack[stack.length - 1].el : main; }
          function fits() { return content.scrollHeight <= content.clientHeight + 0.5; }

          function dropEmptyWrappers() {
            for (var i = stack.length - 1; i >= 0; i--) {
              var w = stack[i].el;
              if (!w.querySelector('*:not(colgroup):not(col)') && !w.textContent.trim() && w.parentNode) { w.parentNode.removeChild(w); }
              else { break; }
            }
          }

          function startSheet() {
            sheet = newSheetElement();
            content = sheet.querySelector('.content');
            main = content.querySelector('main');
            placed = 0;
            var parent = main;
            for (var i = 0; i < stack.length; i++) {
              var w = shallow(stack[i].src);
              parent.appendChild(w);
              stack[i].el = w;
              parent = w;
              // Шапка таблицы повторяется на каждом листе, как при печати.
              if (stack[i].head) { w.appendChild(stack[i].head.cloneNode(true)); }
            }
          }

          function nextSheet() { dropEmptyWrappers(); startSheet(); }

          function elementChildren(node) {
            var r = [];
            for (var c = node.firstChild; c; c = c.nextSibling) { if (c.nodeType === 1) { r.push(c); } }
            return r;
          }

          function place(node) {
            if (node.nodeType === 3) {
              if (node.textContent.trim() !== '') { host().appendChild(node.cloneNode(true)); }
              return;
            }
            if (node.nodeType !== 1) { return; }
            var tag = node.tagName;
            if (placed > 0 && breaksBefore(node)) { nextSheet(); }
            var clone = node.cloneNode(true);
            var parent = host();
            parent.appendChild(clone);
            var notes = addFootnotes(clone);
            if (fits()) { placed++; return; }
            removeFootnotes(notes);
            parent.removeChild(clone);

            if (CONTAINERS.test(tag) && elementChildren(node).length > 0) {
              var w = shallow(node);
              parent.appendChild(w);
              var entry = { src: node, el: w, head: null };
              stack.push(entry);
              for (var c = node.firstChild; c; c = c.nextSibling) {
                place(c);
                if (tag === 'TABLE' && c.nodeType === 1 && c.tagName === 'THEAD') { entry.head = c; }
              }
              stack.pop();
              return;
            }
            if (placed > 0) { nextSheet(); }
            var single = node.cloneNode(true);
            host().appendChild(single);
            addFootnotes(single);
            placed++;
          }

          function placeWithHeadings(container) {
            var kids = elementChildren(container);
            for (var i = 0; i < kids.length; i++) {
              var el = kids[i];
              // Заголовок не остаётся один внизу листа: переносится вместе со следующим блоком.
              if (HEADINGS.test(el.tagName) && i + 1 < kids.length && placed > 0) {
                var h = el.cloneNode(true), n = kids[i + 1].cloneNode(true), p = host();
                p.appendChild(h); p.appendChild(n);
                var ok = fits();
                p.removeChild(n); p.removeChild(h);
                if (!ok && !CONTAINERS.test(kids[i + 1].tagName)) { nextSheet(); }
              }
              place(el);
            }
          }

          function fill() {
            var total = sheets.length;
            for (var i = 0; i < sheets.length; i++) {
              var sub = function (t) {
                return (t || '').replace(/\{page\}/g, i + 1).replace(/\{pages\}/g, total)
                  .replace(/\{title\}/g, CONFIG.title).replace(/\{date\}/g, CONFIG.date);
              };
              sheets[i].querySelector('.head').textContent = sub(CONFIG.header);
              sheets[i].querySelector('.foot').textContent = sub(CONFIG.footer || '{page}');
            }
            document.documentElement.setAttribute('data-pages', String(total));
          }

          // Лист шире окна (узкая панель рядом с текстом) — масштаб по ширине, чтобы не было горизонтальной прокрутки.
          function fit() {
            pagesEl.style.zoom = 1;
            var w = sheets.length ? sheets[0].offsetWidth : 0;
            var k = w > 0 ? Math.min(1, (document.documentElement.clientWidth - 24) / w) : 1;
            pagesEl.style.zoom = k > 0.1 ? k : 0.1;
          }

          function paginate() {
            var y = window.pageYOffset;
            pagesEl.style.zoom = 1; // измерения — в натуральную величину
            pagesEl.innerHTML = '';
            sheets = []; stack = [];
            extractFootnotes();
            startSheet();
            placeWithHeadings(srcMain);
            fill();
            fit();
            window.scrollTo(0, y);
          }

          window.addEventListener('resize', fit);

          window.ditaSetContent = function (html) { srcMain.innerHTML = html; footnotes = null; paginate(); };
          window.ditaPaginate = paginate;
          window.ditaSetContent(INITIAL);
        })();
        """;
}
