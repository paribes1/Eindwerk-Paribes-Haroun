"""Create the Dutch implementation manual with the bundled python-docx runtime.

Run from the Unity project root. Visual QA is done separately with Word PDF export.
"""
from pathlib import Path
import json

from docx import Document
from docx.shared import Inches, Pt, RGBColor
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.enum.table import WD_TABLE_ALIGNMENT, WD_CELL_VERTICAL_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / 'Documentation' / 'Playermechanics en multiplayer.docx'
CONTENT = Path(__file__).with_name('manual_content.json')


def cell_fill(cell, fill):
    pr = cell._tc.get_or_add_tcPr()
    shading = OxmlElement('w:shd')
    shading.set(qn('w:fill'), fill)
    pr.append(shading)


def table(doc, spec):
    columns = spec['columns']
    rows = spec['rows']
    widths = spec.get('widths', [6.7 / len(columns)] * len(columns))
    result = doc.add_table(rows=1, cols=len(columns))
    result.alignment = WD_TABLE_ALIGNMENT.CENTER
    result.autofit = False
    for index, width in enumerate(widths):
        result.columns[index].width = Inches(width)
    properties = result._tbl.tblPr
    borders = OxmlElement('w:tblBorders')
    for side in ('top', 'left', 'bottom', 'right', 'insideH', 'insideV'):
        item = OxmlElement('w:' + side)
        item.set(qn('w:val'), 'single')
        item.set(qn('w:sz'), '4')
        item.set(qn('w:color'), 'D9D9D9')
        borders.append(item)
    properties.append(borders)
    margins = OxmlElement('w:tblCellMar')
    for side in ('top', 'bottom', 'left', 'right'):
        item = OxmlElement('w:' + side)
        item.set(qn('w:w'), '95' if side in ('top', 'bottom') else '115')
        item.set(qn('w:type'), 'dxa')
        margins.append(item)
    properties.append(margins)
    header = result.rows[0]
    repeat = OxmlElement('w:tblHeader')
    header._tr.get_or_add_trPr().append(repeat)
    for i, label in enumerate(columns):
        header.cells[i].text = label
        cell_fill(header.cells[i], '24445A')
        for run in header.cells[i].paragraphs[0].runs:
            run.bold = True
            run.font.color.rgb = RGBColor(255, 255, 255)
    for index, values in enumerate(rows):
        cells = result.add_row().cells
        for i, value in enumerate(values):
            cells[i].text = str(value)
            if index % 2:
                cell_fill(cells[i], 'F0F4F7')
    for row in result.rows:
        row._tr.get_or_add_trPr().append(OxmlElement('w:cantSplit'))
        for i, cell in enumerate(row.cells):
            cell.width = Inches(widths[i])
            cell.vertical_alignment = WD_CELL_VERTICAL_ALIGNMENT.CENTER
            for p in cell.paragraphs:
                if i in spec.get('centerColumns', []):
                    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
                p.paragraph_format.space_after = Pt(1)
                p.paragraph_format.line_spacing = 1.05
                for run in p.runs:
                    run.font.size = Pt(10)
    doc.add_paragraph().paragraph_format.space_after = Pt(0)


def main():
    data = json.loads(CONTENT.read_text(encoding='utf-8'))
    doc = Document()
    section = doc.sections[0]
    section.page_width, section.page_height = Inches(8.5), Inches(11)
    section.left_margin = section.right_margin = Inches(.85)
    section.top_margin = section.bottom_margin = Inches(.75)
    for name in ('Normal', 'Title', 'Subtitle', 'Heading 1', 'Heading 2', 'Heading 3'):
        style = doc.styles[name]
        style.font.name = 'Calibri'
        style.font.color.rgb = RGBColor(0, 0, 0)
    normal = doc.styles['Normal']
    normal.font.size = Pt(11)
    normal.paragraph_format.space_after = Pt(7)
    normal.paragraph_format.line_spacing = 1.08
    doc.styles['Title'].font.size = Pt(27)
    doc.styles['Title'].paragraph_format.space_after = Pt(12)
    doc.styles['Subtitle'].font.size = Pt(11)
    doc.styles['Subtitle'].paragraph_format.space_after = Pt(14)
    doc.styles['Heading 1'].font.size = Pt(17)
    doc.styles['Heading 1'].paragraph_format.space_before = Pt(15)
    doc.styles['Heading 1'].paragraph_format.space_after = Pt(8)
    doc.styles['Heading 2'].font.size = Pt(12)
    doc.styles['Heading 2'].paragraph_format.space_before = Pt(11)
    doc.styles['Heading 2'].paragraph_format.space_after = Pt(6)
    for name in ('Title', 'Subtitle', 'Heading 1', 'Heading 2', 'Heading 3'):
        ppr = doc.styles[name].element.pPr
        if ppr is not None:
            for border in list(ppr.findall(qn('w:pBdr'))):
                ppr.remove(border)
    doc.core_properties.title = data['title']
    doc.core_properties.subject = 'Technische documentatie van spelermechanics en multiplayer in Unity'
    doc.core_properties.author = 'Eindwerk Paribes en Haroun'
    doc.core_properties.keywords = 'Unity capsule spelermechanics multiplayer LAN Netcode'
    page_break_pending = False
    for block in data['blocks']:
        kind = block['type']
        if kind == 'title':
            doc.add_paragraph(block['text'], 'Title')
        elif kind == 'subtitle':
            doc.add_paragraph(block['text'], 'Subtitle')
        elif kind == 'heading':
            heading = doc.add_heading(block['text'], block.get('level', 1))
            if page_break_pending:
                heading.paragraph_format.page_break_before = True
                page_break_pending = False
        elif kind == 'paragraph':
            doc.add_paragraph(block['text'])
        elif kind == 'table':
            table(doc, block)
        elif kind == 'steps':
            for index, step in enumerate(block['items'], 1):
                paragraph = doc.add_paragraph(f'{index}. {step}')
                paragraph.paragraph_format.left_indent = Inches(.25)
                paragraph.paragraph_format.first_line_indent = Inches(-.25)
        elif kind == 'bullets':
            for item in block['items']:
                doc.add_paragraph(item, 'List Bullet')
        elif kind == 'pagebreak':
            page_break_pending = True
        else:
            raise ValueError(kind)
    OUTPUT.parent.mkdir(exist_ok=True)
    doc.save(OUTPUT)
    print(OUTPUT)


if __name__ == '__main__':
    main()
