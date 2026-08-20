// ---------------------------------------------------------------------------
// Hands the browser a text file to save.
//
// Its own module rather than four lines inside a component because the object
// URL has to be revoked afterwards, and that is exactly the line that gets
// dropped when this is written inline — each forgotten one pins the whole blob
// in memory for the life of the document.
//
// Nothing here touches the network: the file is built from a string already in
// the page, which is the property the Config.h download depends on (it carries
// secrets that must never be uploaded to render).
// ---------------------------------------------------------------------------

export function downloadTextFile(
  fileName: string,
  contents: string,
  // Defaults to text/plain because that is the type least likely to make a
  // browser PREVIEW the file instead of saving it — which is what the Config.h
  // download wants from a C header. A caller with a type the browser handles
  // properly, such as text/csv, should name it: some spreadsheet integrations
  // and OS "open with" rules key off the MIME type rather than the extension.
  mimeType: string = 'text/plain;charset=utf-8',
): void {
  const blob = new Blob([contents], { type: mimeType })
  const url = URL.createObjectURL(blob)

  const link = document.createElement('a')
  link.href = url
  link.download = fileName

  // Firefox only honours a click on a link that is in the document.
  document.body.appendChild(link)
  link.click()
  document.body.removeChild(link)

  URL.revokeObjectURL(url)
}
