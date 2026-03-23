import "@testing-library/jest-dom/vitest";

// jsdom does not implement HTMLDialogElement.showModal / close.
// Polyfill them so Modal.tsx effects don't throw in unit tests.
HTMLDialogElement.prototype.showModal = function () {
  this.setAttribute("open", "");
};
HTMLDialogElement.prototype.close = function () {
  this.removeAttribute("open");
};
