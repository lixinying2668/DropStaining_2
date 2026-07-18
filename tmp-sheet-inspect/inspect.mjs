import { FileBlob, SpreadsheetFile } from '@oai/artifact-tool';

const path = 'D:/滴染病理系统/files/3_条码/冰免条码相关/2026通灵一类产品仪器代码 2026.05.11最新.xlsx';
const workbook = await SpreadsheetFile.importXlsx(await FileBlob.load(path));

const sheets = await workbook.inspect({
  kind: 'sheet',
  include: 'id,name',
  maxChars: 5000,
});
console.log('SHEETS');
console.log(sheets.ndjson);

const summary = await workbook.inspect({
  kind: 'workbook,sheet,table',
  maxChars: 12000,
  tableMaxRows: 12,
  tableMaxCols: 12,
  tableMaxCellChars: 100,
});
console.log('SUMMARY');
console.log(summary.ndjson);

const names = await workbook.inspect({
  kind: 'match',
  searchTerm: '名称|一抗|抗体|P01|P02',
  options: { useRegex: true, maxResults: 200 },
  maxChars: 20000,
});
console.log('MATCHES');
console.log(names.ndjson);
