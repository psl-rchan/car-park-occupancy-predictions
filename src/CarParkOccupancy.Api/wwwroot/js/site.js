(function () {
    var input = document.getElementById("park-filter");
    var shown = document.getElementById("shown-count");
    if (!input) {
        return;
    }

    var rows = Array.prototype.slice.call(document.querySelectorAll("[data-car-park]"));

    function apply() {
        var query = input.value.trim().toLowerCase();
        var visible = 0;
        rows.forEach(function (row) {
            var code = (row.getAttribute("data-car-park") || "").toLowerCase();
            var show = query.length === 0 || code.indexOf(query) !== -1;
            row.hidden = !show;
            if (show) {
                visible += 1;
            }
        });
        if (shown) {
            shown.textContent = String(visible);
        }
    }

    input.addEventListener("input", apply);
})();
