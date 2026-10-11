/**
 * School sitemap files, ids 1..SCHOOL_SITEMAP_FILES (plus id 0 for static pages).
 * Next fixes the id list at build time, when the catalog is usually unreachable; each file holds up to
 * 10 000 URLs, so the total capacity is 50 000 schools.
 */
export const SCHOOL_SITEMAP_FILES = 5;
/** Id of the preschool profile file (SPEC-D). It follows the school files; one file holds up to 10 000 preschools. */
export const PRESCHOOL_SITEMAP_ID = SCHOOL_SITEMAP_FILES + 1;
