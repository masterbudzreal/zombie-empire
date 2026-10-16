<?php
// Copy this file to config.php on the hosting (never commit config.php).
// Gmail: Google Account -> Security -> 2-Step Verification -> App passwords -> create one for "Zombie Empire".
return array(
    'notify_to' => 'marcofatti99@gmail.com',      // who receives the "new subscriber" emails
    'smtp_host' => 'smtp.gmail.com',
    'smtp_port' => 587,
    'smtp_user' => 'marcofatti99@gmail.com',      // Gmail account that sends the emails
    'smtp_pass' => 'PASTE_APP_PASSWORD_HERE',     // 16-character app password, without spaces
    'secret'    => 'CHANGE_ME_TO_ANY_RANDOM_TEXT', // used to anonymize IP addresses for rate limiting
);
