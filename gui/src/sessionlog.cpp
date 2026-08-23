// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#include <sessionlog.h>
#include <chiaki/log.h>

#include <QStandardPaths>
#include <QDir>
#include <QRegularExpression>
#include <QDateTime>
#include <QFile>
#include <QPair>
#include <QVector>
#include <QThread>

static void LogCb(ChiakiLogLevel level, const char *msg, void *user);
static QString SanitizeLogMessage(const QString &msg);

SessionLog::SessionLog(StreamSession *session, uint32_t level_mask, const QString &filename, bool sanitize)
	: session(session), shutdown(false), sanitize(sanitize)
{
	chiaki_log_init(&log, level_mask, LogCb, this);

	if(filename.isEmpty())
	{
		file = nullptr;
		CHIAKI_LOGI(&log, "Logging to file disabled");
	}
	else
	{
		file = new QFile(filename);
		if(!file->open(QIODevice::ReadWrite))
		{
			delete file;
			file = nullptr;
			CHIAKI_LOGI(&log, "Failed to open file %s for logging", filename.toLocal8Bit().constData());
		}
		else
		{
			CHIAKI_LOGI(&log, "Logging to file %s", filename.toLocal8Bit().constData());
		}
	}

	CHIAKI_LOGI(&log, "Chiaki Version " CHIAKI_VERSION);
}

SessionLog::~SessionLog()
{
	PrepareShutdown();
}

void SessionLog::PrepareShutdown()
{
	// Signal that we're shutting down - no more logging allowed
	shutdown.storeRelaxed(true);

	// Wait a moment for any in-flight log calls to complete
	QThread::msleep(10);

	// Now safe to clean up
	QMutexLocker lock(&file_mutex);
	if(file)
	{
		file->flush();
		delete file;
		file = nullptr;
	}
}

void SessionLog::Log(ChiakiLogLevel level, const char *msg)
{
	QString sanitized_msg = sanitize ? SanitizeLogMessage(QString::fromUtf8(msg)) : QString::fromUtf8(msg);
	QByteArray sanitized_utf8 = sanitized_msg.toUtf8();
	const char *output_msg = sanitized_utf8.constData();

	// Check if we're shutting down - if so, skip file logging
	if(shutdown.loadRelaxed())
	{
		chiaki_log_cb_print(level, output_msg, nullptr);
		return;
	}

	chiaki_log_cb_print(level, output_msg, nullptr);

	QMutexLocker lock(&file_mutex);
	if(file && !shutdown.loadRelaxed())
	{
		static const QString date_format = "yyyy-MM-dd HH:mm:ss:zzzzzz";
		QString str = QString("[%1] [%2] %3\n").arg(
				QDateTime::currentDateTime().toString(date_format),
				QString(chiaki_log_level_char(level)),
				sanitized_msg);

		file->write(str.toLocal8Bit());
		file->flush();
	}
}

class SessionLogPrivate
{
	public:
		static void Log(SessionLog *log, ChiakiLogLevel level, const char *msg) { log->Log(level, msg); }
};

static void LogCb(ChiakiLogLevel level, const char *msg, void *user)
{
	auto log = reinterpret_cast<SessionLog *>(user);
	SessionLogPrivate::Log(log, level, msg);
}

// Heap-allocated regexes that intentionally outlive static destruction,
// preventing use-after-free when background threads (e.g. takion) log
// while the process is shutting down.
// PP320: a hexdump row, redacted whole, and applied before every rule below.
//
// The other rules read a log line. chiaki_log_hexdump does not write log lines - it writes an
// offset, sixteen bytes as space-separated pairs, and an ASCII gutter of every printable character.
// session.c dumps the session request and the response header that way, so RP-Registkey and
// RP-Nonce pass through in two shapes at once, as hex and as text. The long-hex rule cannot fire
// because the pairs are separated, and the labelled-secret rule names neither field.
//
// Whole, and not field by field: the gutter is sixteen characters wide and a secret is longer, so
// the tail of one continues on the next row with no label above it. PP88's finding applies exactly
// - a partial redaction is worse than none, because a reader scanning for leaks skips a line that
// says redacted.
//
// This text is duplicated in app/Session/SessionLogSanitizer.cs and the .NET selftest asserts the
// two are character-identical.
static const QRegularExpression &sanitize_hexdump_row_re()
{
	static const auto *re = new QRegularExpression(
		R"((?:[0-9a-fA-F]{2} ){8,}[^\n]*)");
	return *re;
}
static const QRegularExpression &sanitize_ipv4_re()
{
	static const auto *re = new QRegularExpression(
		R"(\b(?:(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\b)");
	return *re;
}
// PP88: the first alternative used to be (?:[0-9A-Fa-f]{1,4}:){3,7}[0-9A-Fa-f]{1,4}, which stops
// at the first "::" it meets - the engine takes the leftmost match, so fd00:1234:5678:9abc::1 was
// replaced down to <redacted-ipv6>::1 and the marker made the line look handled. A reader scanning
// a log for leaks skips a line that says redacted, which is what made a partial redaction worse
// than none.
//
// The shape below matches a whole token instead: an optional group, then three or more runs of one
// or two colons with an optional group after each. Three is the floor that keeps a clock out of it
// - 20:10:38 is two runs and does not match, while aa:bb:cc:dd is four and does, exactly as the old
// pattern did. The second alternative is still there for a token whose only separator is "::",
// which the first cannot reach.
//
// This text is duplicated in app/Session/SessionLogSanitizer.cs and the .NET selftest asserts the
// two are character-identical, because two clients that redact differently produce logs that
// cannot be compared with the ones users already have.
static const QRegularExpression &sanitize_ipv6_re()
{
	static const auto *re = new QRegularExpression(
		R"((?<![0-9A-Za-z])\[?(?:[0-9A-Fa-f]{0,4}(?::{1,2}[0-9A-Fa-f]{0,4}){3,}|[0-9A-Fa-f]{0,4}::[0-9A-Fa-f]{0,4}(?::[0-9A-Fa-f]{0,4})*)\]?(?![0-9A-Za-z]))");
	return *re;
}
static const QRegularExpression &sanitize_labeled_secret_re()
{
	static const auto *re = new QRegularExpression(
		R"((((?:console|host|server|session|account|psn|public|remote)\s+(?:id|ip|address)|duid)\s*:\s*)([^\s,;]+))",
		QRegularExpression::CaseInsensitiveOption);
	return *re;
}
static const QRegularExpression &sanitize_session_id_token_re()
{
	static const auto *re = new QRegularExpression(
		R"((session\s+id\s+)([A-Za-z0-9+/=_-]{8,}))",
		QRegularExpression::CaseInsensitiveOption);
	return *re;
}
static const QRegularExpression &sanitize_uuid_re()
{
	static const auto *re = new QRegularExpression(
		R"(\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-5][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}\b)");
	return *re;
}
static const QRegularExpression &sanitize_long_hex_re()
{
	static const auto *re = new QRegularExpression(
		R"(\b[a-fA-F0-9]{16,}\b)");
	return *re;
}
static const QRegularExpression &sanitize_account_id_re()
{
	static const auto *re = new QRegularExpression(
		R"((account(?:_id)?\s*=\s*)([^\s,;]+))", QRegularExpression::CaseInsensitiveOption);
	return *re;
}
static const QRegularExpression &sanitize_duid_re()
{
	static const auto *re = new QRegularExpression(
		R"((duid\s*=\s*)([^\s,;]+))", QRegularExpression::CaseInsensitiveOption);
	return *re;
}
static const QRegularExpression &sanitize_session_id_eq_re()
{
	static const auto *re = new QRegularExpression(
		R"((session\s+id\s*=\s*)([^\s,;]+))", QRegularExpression::CaseInsensitiveOption);
	return *re;
}

static QString SanitizeLogMessage(const QString &msg)
{
	QString sanitized = msg;

	// PP320 first, so a row that reaches the rules below is already redacted and none of them can
	// turn it into something this one would no longer match.
	sanitized.replace(sanitize_hexdump_row_re(), QStringLiteral("<redacted-hexdump>"));

	sanitized.replace(sanitize_ipv4_re(), QStringLiteral("<redacted-ipv4>"));
	sanitized.replace(sanitize_ipv6_re(), QStringLiteral("<redacted-ipv6>"));
	sanitized.replace(sanitize_labeled_secret_re(), QStringLiteral("\\1<redacted>"));
	sanitized.replace(sanitize_account_id_re(), QStringLiteral("\\1<redacted>"));
	sanitized.replace(sanitize_duid_re(), QStringLiteral("\\1<redacted>"));
	sanitized.replace(sanitize_session_id_eq_re(), QStringLiteral("\\1<redacted>"));
	sanitized.replace(sanitize_session_id_token_re(), QStringLiteral("\\1<redacted>"));
	sanitized.replace(sanitize_uuid_re(), QStringLiteral("<redacted-uuid>"));

	// Catch unlabeled long hex identifiers, which commonly occur in console IDs and device UIDs.
	sanitized.replace(sanitize_long_hex_re(), QStringLiteral("<redacted-hex>"));

	return sanitized;
}

#define KEEP_LOG_FILES_COUNT 5

QString GetLogBaseDir()
{
	auto base_dir = QStandardPaths::writableLocation(QStandardPaths::AppDataLocation);
	if(base_dir.isEmpty())
		return QString();

	QDir dir(base_dir);
	if(!dir.mkpath("log"))
		return QString();
	if(!dir.cd("log"))
		return QString();

	return dir.absolutePath();
}

QString CreateLogFilename()
{
	static const QString date_format = "yyyy-MM-dd_HH-mm-ss-zzzzzz";
	static const QString session_log_wildcard = "chiaki_session_*.log";
	static const QRegularExpression session_log_regex("chiaki_session_(.*).log");

	QString dir_str = GetLogBaseDir();
	if(dir_str.isEmpty())
		return QString();
	QDir dir = QDir(dir_str);

	dir.setNameFilters({ session_log_wildcard });
	auto existing_files = dir.entryList();
	QVector<QPair<QString, QDateTime>> existing_files_date;
	existing_files_date.resize(existing_files.count());
	std::transform(existing_files.begin(), existing_files.end(), existing_files_date.begin(), [](const QString &filename) {
		QDateTime date;
		auto match = session_log_regex.match(filename);
		if(match.hasMatch())
			date = QDateTime::fromString(match.captured(1), date_format);
		return QPair<QString, QDateTime>(filename, date);
	});
	std::sort(existing_files_date.begin(), existing_files_date.end(), [](const QPair<QString, QDateTime> &a, const QPair<QString, QDateTime> &b) {
		return a.second > b.second;
	});

	for(int i=KEEP_LOG_FILES_COUNT; i<existing_files_date.count(); i++)
	{
		const auto &pair = existing_files_date[i];
		if(!pair.second.isValid())
			break;
		dir.remove(pair.first);
	}

	QString filename = "chiaki_session_" + QDateTime::currentDateTime().toString(date_format) + ".log";
	return dir.absoluteFilePath(filename);
}
