import Foundation

enum DotNetJSONCoding {
    static let dotNetMinDate = Date(timeIntervalSince1970: -62135596800)

    private static let posixLocale = Locale(identifier: "en_US_POSIX")

    private static let decodingFormatters: [DateFormatter] = [
        makeFormatter("yyyy-MM-dd'T'HH:mm:ss.SSSSSSSXXXXX", timeZone: TimeZone(secondsFromGMT: 0)),
        makeFormatter("yyyy-MM-dd'T'HH:mm:ssXXXXX", timeZone: TimeZone(secondsFromGMT: 0)),
        makeFormatter("yyyy-MM-dd'T'HH:mm:ss.SSSSSSS", timeZone: .current),
        makeFormatter("yyyy-MM-dd'T'HH:mm:ss", timeZone: .current)
    ]

    private static let encodingFormatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.calendar = Calendar(identifier: .gregorian)
        formatter.locale = posixLocale
        formatter.timeZone = .current
        formatter.dateFormat = "yyyy-MM-dd'T'HH:mm:ss.SSSSSSSXXXXX"
        return formatter
    }()

    static func makeEncoder() -> JSONEncoder {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted]
        encoder.dateEncodingStrategy = .custom { date, encoder in
            var container = encoder.singleValueContainer()
            try container.encode(encodingFormatter.string(from: date))
        }
        return encoder
    }

    static func makeDecoder() -> JSONDecoder {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .custom { decoder in
            let container = try decoder.singleValueContainer()
            let value = try container.decode(String.self)

            for formatter in decodingFormatters {
                if let date = formatter.date(from: value) {
                    return date
                }
            }

            throw DecodingError.dataCorruptedError(
                in: container,
                debugDescription: "Unsupported .NET DateTime value: \(value)"
            )
        }
        return decoder
    }

    private static func makeFormatter(_ format: String, timeZone: TimeZone?) -> DateFormatter {
        let formatter = DateFormatter()
        formatter.calendar = Calendar(identifier: .gregorian)
        formatter.locale = posixLocale
        formatter.timeZone = timeZone
        formatter.dateFormat = format
        return formatter
    }
}
