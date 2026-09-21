namespace DitaStudio.Core.Publishing;

/// <summary>Стили публикации. Встроены в приложение, чтобы сборка не зависела от внешних файлов.</summary>
public static class Assets
{
    public const string StyleSheet = """
:root {
  --text: #1a1c1f;
  --muted: #5b6472;
  --line: #dfe3e8;
  --accent: #1f5fa9;
  --code-bg: #f5f6f8;
  --note-bg: #eef4fb;
  --warn-bg: #fdf3e2;
  --danger-bg: #fdecec;
  --sidebar: #f7f8fa;
}
* { box-sizing: border-box; }
body {
  margin: 0;
  color: var(--text);
  background: #fff;
  font: 16px/1.6 "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
}
.layout { display: flex; align-items: flex-start; min-height: 100vh; }
.toc {
  width: 320px; flex: 0 0 320px; padding: 24px 20px; background: var(--sidebar);
  border-right: 1px solid var(--line); position: sticky; top: 0; max-height: 100vh; overflow: auto;
}
.toc h2 { font-size: 14px; text-transform: uppercase; letter-spacing: .08em; color: var(--muted); margin: 0 0 12px; }
.toc ul { list-style: none; margin: 0; padding-left: 14px; }
.toc > ul { padding-left: 0; }
.toc li { margin: 3px 0; }
.toc a { color: var(--text); text-decoration: none; display: block; padding: 3px 6px; border-radius: 4px; }
.toc a:hover { background: #e8ecf1; }
.toc a.current { background: var(--accent); color: #fff; }
.toc .head > span { display: block; padding: 3px 6px; color: var(--muted); font-weight: 600; }
main { flex: 1 1 auto; padding: 32px 48px 96px; max-width: 900px; }
h1, h2, h3, h4, h5, h6 { line-height: 1.25; margin: 1.6em 0 .5em; font-weight: 600; }
h1 { font-size: 30px; margin-top: 0; }
h2 { font-size: 23px; }
h3 { font-size: 19px; }
h4 { font-size: 17px; }
p { margin: .7em 0; }
a { color: var(--accent); }
.shortdesc { font-size: 17px; color: var(--muted); margin-bottom: 1.2em; }
.section { margin: 1.4em 0; }
.section > .title { font-size: 19px; font-weight: 600; margin: 1.4em 0 .4em; }
code, .codeph, .tt, .synph, .filepath, .userinput, .systemoutput, .varname, .parmname, .apiname, .option, .cmdname, .msgnum, .msgph {
  font-family: "Cascadia Mono", Consolas, "Courier New", monospace; font-size: .92em;
}
.userinput { font-weight: 600; }
pre, .codeblock, .screen, .msgblock, .pre, .lines {
  background: var(--code-bg); border: 1px solid var(--line); border-radius: 6px;
  padding: 12px 14px; overflow-x: auto; white-space: pre; font-family: "Cascadia Mono", Consolas, monospace;
  font-size: .9em; line-height: 1.5;
}
.lines { white-space: pre-wrap; }
.uicontrol { font-weight: 600; }
.menucascade > .uicontrol + .uicontrol::before { content: " → "; font-weight: 400; }
.term { font-style: italic; }
.note { background: var(--note-bg); border-left: 4px solid var(--accent); padding: 10px 14px; margin: 1em 0; border-radius: 0 6px 6px 0; }
/* Полоса на полях у элементов с непустым атрибутом rev — штатная DITA-пометка изменений. */
.rev-changed { border-left: 3px solid #d4380d; padding-left: 8px; }
/* Track changes (status="new"/"deleted") — видно только в предпросмотре редактора: сам вывод
   публикации помеченное на удаление содержимое не показывает вовсе. */
.tc-inserted { background: #e6f4ea; border-left: 3px solid #2e8b3d; padding-left: 8px; }
.tc-deleted { text-decoration: line-through; opacity: .6; background: #fbe9e7; border-left: 3px solid #8b2e2e; padding-left: 8px; }
.note .label { font-weight: 600; margin-right: .4em; }
.note.caution, .note.attention, .note.warning, .note.notice { background: var(--warn-bg); border-left-color: #c58217; }
.note.danger { background: var(--danger-bg); border-left-color: #c0392b; }
table { border-collapse: collapse; margin: 1.2em 0; width: 100%; font-size: .95em; }
th, td { border: 1px solid var(--line); padding: 7px 10px; text-align: left; vertical-align: top; }
th { background: #f0f2f5; font-weight: 600; }
.table-title, .fig-title { font-weight: 600; margin: 1.2em 0 .4em; }
.generated-title { font-weight: 600; font-size: 1.05em; margin: 1.5em 0 .4em; }
figure { margin: 1.2em 0; }
img { max-width: 100%; height: auto; }
dl { margin: 1em 0; }
dt { font-weight: 600; margin-top: .8em; }
dd { margin: .2em 0 .2em 24px; }
ol, ul { margin: .7em 0; padding-left: 26px; }
li { margin: .3em 0; }
.steps > li { margin: .9em 0; }
.step .cmd { font-weight: 500; }
.step .info, .step .stepresult, .step .stepxmp { margin: .35em 0; }
.stepresult::before { content: "→ "; color: var(--muted); }
.related-links { margin-top: 2.5em; border-top: 1px solid var(--line); padding-top: 1em; }
.related-links h2 { font-size: 16px; text-transform: uppercase; letter-spacing: .06em; color: var(--muted); }
.related-links ul { list-style: none; padding-left: 0; }
.footnotes { margin-top: 2.5em; border-top: 1px solid var(--line); padding-top: .8em; font-size: .9em; color: var(--muted); }
.index-terms { margin-top: 2.5em; border-top: 1px solid var(--line); padding-top: .8em; }
.index-terms ul { list-style: none; padding-left: 1.2em; }
.index-terms > ul { padding-left: 0; }
.fn-ref { vertical-align: super; font-size: .78em; text-decoration: none; }
.draft-comment { background: #fff7c2; border: 1px dashed #d4b106; padding: 8px 12px; margin: 1em 0; font-size: .92em; }
.hazard { border: 2px solid #c0392b; padding: 12px 14px; margin: 1.2em 0; border-radius: 6px; }
.hazard .typeofhazard { font-weight: 700; text-transform: uppercase; }
.breadcrumbs { color: var(--muted); font-size: .9em; margin-bottom: 1em; }
.breadcrumbs a { color: var(--muted); }
.pager { display: flex; justify-content: space-between; margin-top: 3em; border-top: 1px solid var(--line); padding-top: 1em; font-size: .95em; }
.chapter-heading { margin-top: 3em; padding-top: 1.5em; border-top: 3px solid var(--line); }
.unknown-element { border-left: 3px solid #c0392b; padding-left: 8px; }
@media print {
  .toc, .pager { display: none; }
  main { max-width: none; padding: 0; }
  body { font-size: 11pt; }
  h1 { page-break-before: always; }
  h1:first-of-type { page-break-before: avoid; }
  table, figure, pre { page-break-inside: avoid; }
  /* Ставится через outputclass="page-break-before" на заголовке (title) —
     заголовок при конвертации в PDF/печати начинается с новой страницы. */
  .page-break-before { page-break-before: always; break-before: page; }
  /* Ставится через outputclass="page-break-auto" на table — таблица разрешает
     разрыв внутри себя, а строка шапки (thead) повторяется на каждой странице. */
  table.page-break-auto { page-break-inside: auto; break-inside: auto; }
  thead { display: table-header-group; }
  tfoot { display: table-footer-group; }
}
@media (max-width: 900px) {
  .layout { display: block; }
  .toc { width: auto; position: static; max-height: none; border-right: none; border-bottom: 1px solid var(--line); }
  main { padding: 24px 20px 64px; }
}
""";

    /// <summary>Приближённая имитация вида DOCX — для предпросмотра "как будет выглядеть при
    /// экспорте" (DocumentPane), не для самой публикации. Значения шрифтов/размеров/отступов
    /// подобраны по DocxPublisher.AddStyles (Calibri, размеры Heading1-6, note — серая полоса без
    /// заливки, таблицы — сплошная чёрная сетка) — не пиксель-в-пиксель (реальную пагинацию,
    /// сноски и разрывы страниц Word воспроизводит только сам DOCX-экспорт), но структурно похоже.</summary>
    public const string WordPreviewCss = """
body { font-family: Calibri, "Segoe UI", sans-serif; font-size: 11pt; line-height: 1.3; color: #000; }
main { max-width: 760px; }
h1, h2, h3, h4, h5, h6 { font-weight: bold; color: #000; font-family: Calibri, sans-serif; margin: 12pt 0 6pt; }
h1 { font-size: 18pt; }
h2 { font-size: 16pt; }
h3 { font-size: 14pt; }
h4, h5, h6 { font-size: 12pt; }
.shortdesc { font-size: 11pt; color: #000; font-style: italic; }
a { color: #0563C1; text-decoration: underline; }
.note { background: none; border-left: 1.5pt solid #999999; border-radius: 0; padding: 6pt 10pt; }
.note.caution, .note.attention, .note.warning, .note.notice, .note.danger { background: none; border-left-color: #999999; }
table { border-collapse: collapse; }
th, td { border: 1pt solid #000; padding: 4pt 8pt; }
th { background: #E8E8E8; }
pre, .codeblock, .screen, .msgblock, .pre, .lines { background: #F2F2F2; border: none; border-radius: 0; font-family: Consolas, monospace; }
.hazard { border-radius: 0; }
""";
}
