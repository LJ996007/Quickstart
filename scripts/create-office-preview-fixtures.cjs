// Test-only dependency installed under artifacts/debug/checks/office-preview/fixture-tools.
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../artifacts/debug/checks/office-preview');
const xlsx = require(path.join(root, 'fixture-tools/node_modules/xlsx'));
const book = xlsx.utils.book_new();
const sheet = xlsx.utils.aoa_to_sheet([
  ['项目名称', '数量', '含税金额'], ['中文测试', 0, 1234.50], ['合同条款', 2, 2469], ['复制内容', false, '保留中文与零值']
]);
sheet['!cols'] = [{ wch: 24 }, { wch: 16 }, { wch: 28 }];
sheet.C2.z = '#,##0.00';
sheet.C3.f = 'C2*B3';
xlsx.utils.book_append_sheet(book, sheet, '采购清单');
xlsx.utils.book_append_sheet(book, xlsx.utils.aoa_to_sheet([['第二工作表'], ['切换复制成功']]), '第二页');
for (const type of ['xlsx', 'xls']) xlsx.writeFile(book, path.join(root, `预览 测试.${type}`));
const large = xlsx.utils.book_new();
const rows = Array.from({length: 10000}, (_, i) => [`第 ${i + 1} 行`, i, i * 1.5]);
xlsx.utils.book_append_sheet(large, xlsx.utils.aoa_to_sheet(rows), '一万行');
xlsx.writeFile(large, path.join(root, '万行表格.xlsx'));
console.log('Created XLSX, BIFF8 XLS, and 10,000-row workbook fixtures.');
