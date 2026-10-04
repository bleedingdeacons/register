@manual @ignore
Feature: On the tablet
  Acceptance criteria that only a tablet can check. Reqnroll skips them;
  they are written down because no test host can reach any of them, and
  several are exactly where a change to the app would go wrong unnoticed.

  # Every member has an email, so "nobody can be reached" means a group
  # with no GSR. Checked on the TB330FU on 2026-10-04.
  Scenario: The Yes button greys out when nobody can be reached
    Given a group with no GSR
    When the volunteer opens the group to verify it
    Then Yes is disabled
    And the left button reads Sign-up rather than No

  Scenario: A stand-in must give a name
    Given a group with a GSR
    When "standing in" is ticked and the name left empty
    Then Yes is disabled
    And typing a name enables it

  Scenario: The consent popup names the person being asked
    Given a GSR who accepted an earlier version of the privacy policy
    When the volunteer taps Yes
    Then the popup's title ends with the GSR's anonymous name
    And declining returns to the verify page with nothing registered

  Scenario: Freedom signs a tablet in through its callback
    Given a build whose appsettings.json names a Freedom site
    When the tablet is signed in from API Settings, then Freedom
    Then the browser returns to the app through FreedomCallbackActivity
    And the SMTP and Unity settings change without a restart

  Scenario: Reset Device deletes both logs
    Given a tablet that has registered a group and recorded an acceptance
    When Settings, then Reset Device, is confirmed
    Then registrations.log and compliance.log are gone from Documents

  Scenario: No build carries credentials
    Given any APK built from this repository, Debug or Release
    Then devsettings.json is not among its embedded resources
    And appsettings.json holds only the Freedom site, the logging level and the App name

  Scenario: A tablet that has not signed in to Freedom says so
    Given a freshly installed tablet that has not signed in to Freedom
    Then Mail Settings and API Settings show "Not set" and a sign-in prompt
    And Load Unity says Unity is not set up, rather than failing obscurely

  Scenario: An upgraded tablet forgets what was typed in
    Given a tablet that had SMTP, Unity and Better Stack settings typed in under an earlier build
    When it starts the first build that takes them only from Freedom
    Then the SecureStorage secrets, the three settings files and the compliance address are gone
    And the log says "Removed the settings earlier builds kept on this tablet"

  Scenario: Registration buttons click, on the media volume
    Given a tablet with Touch sounds off and the system volume muted
    When the volunteer taps Yes, No, a type, a day or a group
    Then each tap clicks once, at the media volume
    But Settings, Admin and the diagnostics stay silent

  Scenario: The click can be switched off
    Given Sound on Button Tap is off in Settings
    When the volunteer taps Yes
    Then nothing is heard

  Scenario: No double click when Touch sounds is on
    Given a tablet with Touch sounds on
    When the volunteer taps Yes
    Then one click is heard, Android's own

  Scenario: The logs survive an uninstall
    Given a tablet that has registered a group
    When the app is uninstalled and reinstalled
    Then registrations.log is still in Documents
