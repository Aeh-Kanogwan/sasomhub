// Sasom Hub — site.js
// Minimal progressive-enhancement scripts ported from the prototype. The ticker scroll is
// pure CSS (@keyframes ticker-scroll); JS here only powers the auction countdown and small toggles.

(function () {
  "use strict";

  // ----- Auction countdown (auction detail page) -----
  // Markup contract: an element with [data-countdown] whose value is the remaining seconds, plus
  // child segments #hh / #mm / #ss. Falls back gracefully if the element is absent.
  function initCountdown() {
    var root = document.querySelector("[data-countdown]");
    if (!root) return;

    var total = parseInt(root.getAttribute("data-countdown"), 10);
    if (isNaN(total) || total < 0) return;

    var hh = document.getElementById("hh");
    var mm = document.getElementById("mm");
    var ss = document.getElementById("ss");
    var pad = function (n) { return String(n).padStart(2, "0"); };

    function render() {
      if (hh) hh.textContent = pad(Math.floor(total / 3600));
      if (mm) mm.textContent = pad(Math.floor((total % 3600) / 60));
      if (ss) ss.textContent = pad(total % 60);
    }

    render();
    var timer = setInterval(function () {
      if (total <= 0) { clearInterval(timer); return; }
      total--;
      render();
    }, 1000);
  }

  // ----- Generic toggle helper (auto-renew switch, promo chips) -----
  // Any element with [data-toggle="class"] toggles the given class on itself when clicked.
  function initToggles() {
    document.querySelectorAll("[data-toggle]").forEach(function (el) {
      el.addEventListener("click", function () {
        el.classList.toggle(el.getAttribute("data-toggle") || "on");
      });
    });
  }

  // ----- Copy-to-clipboard helper (referral invite CTA, FR-28) -----
  // Any element with [data-copy="value"] copies that value to the clipboard on click and shows
  // a brief "คัดลอกแล้ว" confirmation on its label. Used by the Credits referral button.
  function initCopy() {
    document.querySelectorAll("[data-copy]").forEach(function (el) {
      el.addEventListener("click", function () {
        var value = el.getAttribute("data-copy") || "";
        if (!value) return;
        var done = function () {
          var original = el.textContent;
          el.textContent = "คัดลอกแล้ว ✓";
          setTimeout(function () { el.textContent = original; }, 1600);
        };
        if (navigator.clipboard && navigator.clipboard.writeText) {
          navigator.clipboard.writeText(value).then(done).catch(done);
        } else {
          done();
        }
      });
    });
  }

  document.addEventListener("DOMContentLoaded", function () {
    initCountdown();
    initToggles();
    initCopy();
  });
})();
