/**
 * House number: digits, an optional letter, and an optional second part after a slash (5, 5A, 12/4B).
 * Shared by the address form (client check on submit) and the page (server check, which always runs).
 */
export const HOUSE_NUMBER_SOURCE = "\\d{1,4}[A-Za-z]?(?:/\\d{1,4}[A-Za-z]?)?";
export const HOUSE_NUMBER_PATTERN = new RegExp(`^${HOUSE_NUMBER_SOURCE}$`);
export const HOUSE_NUMBER_MESSAGE = "Numer domu podaj cyframi, na przykład 5 lub 5A.";
