// Pomocné funkce pro prohlížečku schématu. Jediné, co se z Blazoru volá.
window.dbsviewer = {
    // Nabídne text ke stažení jako soubor. Data vytváří klient, nic se neposílá na server.
    download: function (fileName, content) {
        const blob = new Blob([content], { type: 'text/plain;charset=utf-8' });
        const url = URL.createObjectURL(blob);

        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);

        URL.revokeObjectURL(url);
    }
};
