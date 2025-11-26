/**
 * Windows Version Sorting Utilities
 * Handles intelligent sorting of Windows versions like 22H2, 23H2, 21H2, etc.
 */

/**
 * Converts a Windows version string to a sortable numeric value
 * Supports formats like:
 * - 25H2, 24H2, 23H2, 22H2, 21H2, 20H2 (H-releases)
 * - 2004, 1909, 1903, 1809, etc. (Year-Month releases)
 * - Enterprise LTSC 2021, Enterprise LTSB 2016, etc. (LTSC releases)
 * 
 * @param {string} version - The version string to convert
 * @returns {number} A numeric value for sorting (higher = newer)
 */
function getWindowsVersionSortValue(version) {
    if (!version) return 0;
    
    // Remove "Windows 10" or "Windows 11" or "Enterprise" prefixes if present
    let cleanVersion = version.toLowerCase()
        .replace(/^windows\s+\d+\s+/i, '')
        .replace(/^enterprise\s+/i, '')
        .replace(/^ltsc\s+/i, '')
        .replace(/^ltsb\s+/i, '')
        .trim();
    
    // Extract the actual version number
    const match = cleanVersion.match(/(\d+)(h\d|[0-9]+)?/i);
    if (!match) return 0;
    
    const yearOrBase = parseInt(match[1]);
    const suffix = match[2] ? match[2].toLowerCase() : '';
    
    // H-releases (22H2, 23H2, etc.)
    // These should sort by year first, then by H release
    // 25H2 > 24H2 > 23H2 > 22H2 > 21H2 > 20H2
    if (suffix.match(/h\d/)) {
        const hVersion = parseInt(suffix.substring(1)); // Extract number from 'H2' -> 2
        // Formula: year * 1000 + hVersion * 100
        // This ensures 25H2 (25*1000 + 2*100 = 25200) > 24H2 (24200) > etc.
        return yearOrBase * 1000 + hVersion * 100;
    }
    
    // Year-Month releases (2004, 1909, 1903, etc.)
    // 2004 > 1909 > 1903 > 1809 > etc.
    if (yearOrBase >= 1000) {
        return yearOrBase * 100;
    }
    
    // LTSC/LTSB releases (2021, 2019, 2016, etc.)
    // 2021 > 2019 > 2016
    if (yearOrBase >= 2000) {
        return yearOrBase * 100;
    }
    
    return yearOrBase;
}

/**
 * DataTables custom sorting for Windows versions
 * Register this with DataTables like:
 * $.fn.dataTable.ext.type.order['windows-version-pre'] = function(version) {
 *     return getWindowsVersionSortValue(version);
 * };
 */
if (typeof $ !== 'undefined' && $.fn.dataTable) {
    $.fn.dataTable.ext.type.order['windows-version-pre'] = function(version) {
        return getWindowsVersionSortValue(version);
    };
}

/**
 * Comparator function for native JavaScript sorting
 * Usage: array.sort((a, b) => compareWindowsVersions(a, b))
 */
function compareWindowsVersions(version1, version2) {
    const value1 = getWindowsVersionSortValue(version1);
    const value2 = getWindowsVersionSortValue(version2);
    return value2 - value1; // Descending order (newer first)
}
