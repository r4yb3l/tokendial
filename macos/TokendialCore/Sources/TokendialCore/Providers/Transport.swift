import Foundation

/// One HTTP exchange. Tests substitute a stub; the app uses URLSession with redirects refused.
public protocol Transport: AnyObject {
    func send(_ request: URLRequest) async throws -> (data: Data, response: HTTPURLResponse)
}

/// URLSession that never follows a redirect (a credential goes to the host the spec names and nowhere
/// else), keeps no cookies, and trusts a self-signed certificate only on loopback.
public final class SessionTransport: NSObject, Transport, URLSessionDelegate, URLSessionTaskDelegate {
    private lazy var session: URLSession = {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.httpShouldSetCookies = false
        configuration.httpCookieAcceptPolicy = .never
        configuration.timeoutIntervalForRequest = timeout
        configuration.timeoutIntervalForResource = timeout
        return URLSession(configuration: configuration, delegate: self, delegateQueue: nil)
    }()
    private let timeout: TimeInterval
    private let allowLoopbackSelfSigned: Bool

    public init(timeout: TimeInterval = 15, allowLoopbackSelfSigned: Bool = false) {
        self.timeout = timeout
        self.allowLoopbackSelfSigned = allowLoopbackSelfSigned
    }

    public func send(_ request: URLRequest) async throws -> (data: Data, response: HTTPURLResponse) {
        let (data, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse else { throw UsageError.badResponse(0) }
        return (data, http)
    }

    public func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) {
        completionHandler(nil)
    }

    public func urlSession(_ session: URLSession, task: URLSessionTask, didReceive challenge: URLAuthenticationChallenge, completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void) {
        guard allowLoopbackSelfSigned,
              challenge.protectionSpace.authenticationMethod == NSURLAuthenticationMethodServerTrust,
              Self.isLoopback(challenge.protectionSpace.host),
              let trust = challenge.protectionSpace.serverTrust else {
            completionHandler(.performDefaultHandling, nil)
            return
        }
        completionHandler(.useCredential, URLCredential(trust: trust))
    }

    public static func isLoopback(_ host: String) -> Bool {
        let h = host.trimmingCharacters(in: CharacterSet(charactersIn: "[]"))
        if h == "localhost" || h == "::1" { return true }
        let octets = h.split(separator: ".", omittingEmptySubsequences: false).map { Int($0) }
        return octets.count == 4 && octets.allSatisfy { $0 != nil && (0...255).contains($0!) } && octets[0] == 127
    }
}

public enum HTTP {
    /// 401/403 mean sign in again; anything outside 2xx that is not handled by the caller, redirects included, is a bad response.
    public static func throwUnlessOK(_ response: HTTPURLResponse) throws {
        let status = response.statusCode
        if status == 401 || status == 403 { throw UsageError.needsSignIn() }
        if status < 200 || status >= 300 { throw UsageError.badResponse(status) }
    }

    public static func request(_ url: URL, method: String = "GET", headers: [String: String], body: Data? = nil) -> URLRequest {
        var request = URLRequest(url: url)
        request.httpMethod = method
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        for (name, value) in headers { request.setValue(value, forHTTPHeaderField: name) }
        if let body {
            request.httpBody = body
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        }
        return request
    }
}
