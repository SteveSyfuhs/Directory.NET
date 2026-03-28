export function exportToCsv(data: Record<string, any>[], columns: { field: string; header: string }[], filename: string) {
  const headers = columns.map(c => c.header)
  const rows = data.map(row => columns.map(c => {
    const val = row[c.field]
    if (val == null) return ''
    const str = String(val)
    // Escape CSV: quote fields containing commas, quotes, or newlines
    if (str.includes(',') || str.includes('"') || str.includes('\n')) {
      return '"' + str.replace(/"/g, '""') + '"'
    }
    return str
  }))

  const csv = [headers.join(','), ...rows.map(r => r.join(','))].join('\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = `${filename}.csv`
  link.click()
  URL.revokeObjectURL(url)
}
