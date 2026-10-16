<?php
// Zombie Empire newsletter endpoint.
// POST name, email, consent (+ honeypot "website") -> stores the subscriber and emails the owner
// a notification with the full subscriber list, the new one highlighted.
// POST action=unsubscribe, email -> removes the address.
// Settings: config.php (copy config.example.php). Data: data/subscribers.json (not web-readable).

header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: no-store');

function reply($ok, $message, $extra = array()) {
    echo json_encode(array_merge(array('ok' => $ok, 'message' => $message), $extra));
    exit;
}

if ($_SERVER['REQUEST_METHOD'] !== 'POST') reply(false, 'Use the form on the website.');

$configFile = __DIR__ . '/config.php';
if (!is_file($configFile)) reply(false, 'Newsletter is not configured yet.');
$cfg = require $configFile;

$dataDir = __DIR__ . '/data';
$listFile = $dataDir . '/subscribers.json';
$rateFile = $dataDir . '/rate.json';
if (!is_dir($dataDir)) mkdir($dataDir, 0755, true);

// ---------- input ----------
if (!empty($_POST['website'])) reply(true, 'Thanks!');          // honeypot: bots fill every field
$action = isset($_POST['action']) ? $_POST['action'] : 'subscribe';
$email = strtolower(trim(isset($_POST['email']) ? $_POST['email'] : ''));
$name = trim(isset($_POST['name']) ? $_POST['name'] : '');
$name = mb_substr(preg_replace('/[\x00-\x1F\x7F<>]/u', '', $name), 0, 60);
if (!filter_var($email, FILTER_VALIDATE_EMAIL) || strlen($email) > 120) reply(false, 'Please enter a valid email address.');

// ---------- storage (locked read-modify-write) ----------
function with_json_file($path, $fn) {
    $fp = fopen($path, 'c+');
    if (!$fp) reply(false, 'Server error, please try again later.');
    flock($fp, LOCK_EX);
    $raw = stream_get_contents($fp);
    $data = $raw ? json_decode($raw, true) : array();
    if (!is_array($data)) $data = array();
    $result = $fn($data);
    ftruncate($fp, 0);
    rewind($fp);
    fwrite($fp, json_encode($data, JSON_PRETTY_PRINT | JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES));
    fflush($fp);
    flock($fp, LOCK_UN);
    fclose($fp);
    return $result;
}

if ($action === 'unsubscribe') {
    $removed = with_json_file($listFile, function (&$list) use ($email) {
        $before = count($list);
        $list = array_values(array_filter($list, function ($s) use ($email) { return $s['email'] !== $email; }));
        return $before !== count($list);
    });
    reply(true, $removed ? 'You have been unsubscribed.' : 'This address was not subscribed.');
}

if (empty($_POST['consent'])) reply(false, 'Please tick the box to agree to receive the news.');

// max 5 sign-ups per IP per hour
$ipHash = substr(hash('sha256', ($_SERVER['REMOTE_ADDR'] ?? '') . $cfg['secret']), 0, 16);
$limited = with_json_file($rateFile, function (&$rate) use ($ipHash) {
    $now = time();
    foreach ($rate as $k => $times) {
        $rate[$k] = array_values(array_filter($times, function ($t) use ($now) { return $t > $now - 3600; }));
        if (!$rate[$k]) unset($rate[$k]);
    }
    if (isset($rate[$ipHash]) && count($rate[$ipHash]) >= 5) return true;
    $rate[$ipHash][] = $now;
    return false;
});
if ($limited) reply(false, 'Too many attempts, please try again later.');

$new = array('name' => $name, 'email' => $email, 'date' => gmdate('Y-m-d H:i') . ' UTC');
$state = with_json_file($listFile, function (&$list) use ($new) {
    foreach ($list as $s) if ($s['email'] === $new['email']) return array('duplicate' => true, 'list' => $list);
    $list[] = $new;
    return array('duplicate' => false, 'list' => $list);
});
if ($state['duplicate']) reply(true, "You're already subscribed – see you in the next update!");

// ---------- notification email to the owner ----------
$mailSent = false;
if (!empty($cfg['smtp_pass']) && strpos($cfg['smtp_pass'], 'PASTE') === false) {
    $mailSent = smtp_send($cfg, $cfg['notify_to'], 'Nuovo iscritto alla newsletter di Zombie Empire', notification_html($new, $state['list']));
}
reply(true, "You're in! We'll email you when there's news.", array('notified' => $mailSent));


function h($s) { return htmlspecialchars($s, ENT_QUOTES, 'UTF-8'); }

function notification_html($new, $list) {
    $logo = 'https://cdn.jsdelivr.net/gh/masterbudzreal/zombie-empire@main/site/assets/logo.png';
    $title = 'https://cdn.jsdelivr.net/gh/masterbudzreal/zombie-empire@main/site/assets/title.png';
    $who = $new['name'] !== '' ? h($new['name']) . ' (' . h($new['email']) . ')' : h($new['email']);
    $rows = '';
    foreach ($list as $i => $s) {
        $isNew = $s['email'] === $new['email'];
        $style = $isNew ? 'background:#c81e1e;color:#ffffff;font-weight:bold;' : 'background:' . ($i % 2 ? '#182018' : '#141914') . ';color:#e3e8de;';
        $rows .= '<tr style="' . $style . '">'
            . '<td style="padding:8px 10px;">' . ($i + 1) . '</td>'
            . '<td style="padding:8px 10px;">' . ($s['name'] !== '' ? h($s['name']) : '–') . ($isNew ? ' &nbsp;<span style="background:#ffffff;color:#c81e1e;padding:1px 6px;border-radius:3px;font-size:11px;">NUOVO</span>' : '') . '</td>'
            . '<td style="padding:8px 10px;">' . h($s['email']) . '</td>'
            . '<td style="padding:8px 10px;white-space:nowrap;">' . h($s['date']) . '</td></tr>';
    }
    $count = count($list);
    return '<!doctype html><html><body style="margin:0;background:#0d100d;font-family:Segoe UI,Arial,sans-serif;color:#e3e8de;">'
        . '<table width="100%" cellpadding="0" cellspacing="0" style="background:#0d100d;"><tr><td align="center" style="padding:28px 12px;">'
        . '<table width="600" cellpadding="0" cellspacing="0" style="max-width:600px;background:#141914;border:1px solid #2a352a;border-radius:8px;">'
        . '<tr><td align="center" style="padding:28px 20px 8px;"><img src="' . $logo . '" width="96" height="96" alt="Zombie Empire" style="image-rendering:pixelated;display:block;"></td></tr>'
        . '<tr><td align="center" style="padding:4px 20px 18px;"><img src="' . $title . '" width="420" alt="ZOMBIE EMPIRE" style="max-width:100%;display:block;"></td></tr>'
        . '<tr><td style="padding:0 28px 8px;font-size:18px;line-height:1.5;text-align:center;">'
        . '<b style="color:#ffffff;">' . $who . '</b><br>si è abbonato alle news del tuo gioco <b style="color:#c81e1e;">ZombieEmpire</b></td></tr>'
        . '<tr><td style="padding:6px 28px 20px;font-size:14px;color:#9aa695;text-align:center;">Iscritti totali: <b style="color:#6fb35a;">' . $count . '</b> · ' . h($new['date']) . '</td></tr>'
        . '<tr><td style="padding:0 20px 24px;"><table width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;font-size:14px;border:1px solid #2a352a;">'
        . '<tr style="background:#0b0e0b;color:#6fb35a;text-align:left;"><th style="padding:8px 10px;">#</th><th style="padding:8px 10px;">Nome</th><th style="padding:8px 10px;">Email</th><th style="padding:8px 10px;">Data</th></tr>'
        . $rows . '</table></td></tr>'
        . '<tr><td align="center" style="padding:0 20px 26px;font-size:12px;color:#9aa695;"><a href="https://zombieempire.rf.gd" style="color:#6fb35a;">zombieempire.rf.gd</a> · notifica automatica della newsletter</td></tr>'
        . '</table></td></tr></table></body></html>';
}

// Minimal SMTP client (STARTTLS + AUTH LOGIN), enough for Gmail with an app password.
function smtp_send($cfg, $to, $subject, $html) {
    $fp = @stream_socket_client('tcp://' . $cfg['smtp_host'] . ':' . $cfg['smtp_port'], $errno, $errstr, 15);
    if (!$fp) return false;
    stream_set_timeout($fp, 15);
    $read = function () use ($fp) {
        $out = '';
        while (($line = fgets($fp, 515)) !== false) {
            $out .= $line;
            if (strlen($line) < 4 || $line[3] === ' ') break;
        }
        return $out;
    };
    $cmd = function ($line, $expect) use ($fp, $read) {
        if ($line !== null) fwrite($fp, $line . "\r\n");
        $resp = $read();
        return strpos($resp, (string)$expect) === 0;
    };
    $from = $cfg['smtp_user'];
    $ok = $cmd(null, 220)
        && $cmd('EHLO zombieempire.rf.gd', 250)
        && $cmd('STARTTLS', 220)
        && stream_socket_enable_crypto($fp, true, STREAM_CRYPTO_METHOD_TLS_CLIENT)
        && $cmd('EHLO zombieempire.rf.gd', 250)
        && $cmd('AUTH LOGIN', 334)
        && $cmd(base64_encode($cfg['smtp_user']), 334)
        && $cmd(base64_encode($cfg['smtp_pass']), 235)
        && $cmd('MAIL FROM:<' . $from . '>', 250)
        && $cmd('RCPT TO:<' . $to . '>', 250)
        && $cmd('DATA', 354);
    if ($ok) {
        $headers = 'From: =?UTF-8?B?' . base64_encode('Zombie Empire') . '?= <' . $from . ">\r\n"
            . 'To: <' . $to . ">\r\n"
            . 'Subject: =?UTF-8?B?' . base64_encode($subject) . "?=\r\n"
            . 'Date: ' . date('r') . "\r\n"
            . 'Message-ID: <' . bin2hex(random_bytes(12)) . '@zombieempire.rf.gd>' . "\r\n"
            . "MIME-Version: 1.0\r\nContent-Type: text/html; charset=UTF-8\r\nContent-Transfer-Encoding: base64\r\n";
        fwrite($fp, $headers . "\r\n" . chunk_split(base64_encode($html)) . "\r\n.\r\n");
        $ok = strpos($read(), '250') === 0;
    }
    fwrite($fp, "QUIT\r\n");
    fclose($fp);
    return $ok;
}
