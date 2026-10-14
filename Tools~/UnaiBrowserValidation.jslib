mergeInto(LibraryManager.library, {
  UNAI_ValidationReport: function (pointer) {
    var report = JSON.parse(UTF8ToString(pointer));
    window.unaiValidationReport = report;
    var status = document.getElementById('status');
    var output = document.getElementById('report');
    if (status) status.textContent = 'Browser validation: ' + report.status;
    if (output) output.textContent = JSON.stringify(report, null, 2);
    var button = document.getElementById('run');
    if (button) button.disabled = report.status === 'running';
    fetch('/report', {method: 'POST', headers: {'Content-Type': 'application/json'}, body: JSON.stringify(report)})
      .catch(function () {});
  }
});
