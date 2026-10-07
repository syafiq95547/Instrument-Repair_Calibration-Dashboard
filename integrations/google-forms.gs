/** Install as an "On form submit" trigger on the Form's linked response spreadsheet.
 * Script Properties: DASHBOARD_URL (HTTPS origin), WEBHOOK_KEY (matches server Webhook__Key).
 * Expected question titles: Asset ID, Instrument Name, Department, Next Due Date, Owner.
 * Configure Next Due Date as a date question. Values must resolve to yyyy-MM-dd.
 */
function onFormSubmit(event) {
  if (!event || !event.namedValues) throw new Error('Use a spreadsheet form-submit trigger.');
  const properties = PropertiesService.getScriptProperties();
  const url = properties.getProperty('DASHBOARD_URL');
  const key = properties.getProperty('WEBHOOK_KEY');
  if (!url || !/^https:\/\//.test(url) || !key) throw new Error('Configure HTTPS DASHBOARD_URL and WEBHOOK_KEY in Script Properties.');
  const value = name => (event.namedValues[name] || [''])[0].trim();
  const rawDate = value('Next Due Date');
  let nextDueDate = null;
  if (rawDate) {
    const parsed = new Date(rawDate);
    if (isNaN(parsed.getTime())) throw new Error('Next Due Date must be a valid date.');
    nextDueDate = Utilities.formatDate(parsed, SpreadsheetApp.getActive().getSpreadsheetTimeZone(), 'yyyy-MM-dd');
  }
  const payload = { assetId: value('Asset ID'), instrumentName: value('Instrument Name'), department: value('Department'), nextDueDate, owner: value('Owner') };
  const response = UrlFetchApp.fetch(url.replace(/\/$/, '') + '/api/webhook/form-submit', {
    method: 'post', contentType: 'application/json', headers: { 'X-Webhook-Key': key },
    payload: JSON.stringify(payload), muteHttpExceptions: true
  });
  if (response.getResponseCode() < 200 || response.getResponseCode() >= 300) {
    // Do not log headers, keys, or personally identifying form contents.
    throw new Error('Dashboard rejected the submission (HTTP ' + response.getResponseCode() + '). Check asset ID uniqueness and server configuration.');
  }
}
