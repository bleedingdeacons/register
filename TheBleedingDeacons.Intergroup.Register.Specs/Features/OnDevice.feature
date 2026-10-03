@manual @ignore
Feature: On the tablet
  Acceptance criteria that only a tablet can check. Reqnroll skips them;
  they are written down because no test host can reach any of them, and
  several are exactly where a change to the app would go wrong unnoticed.

  Scenario: The Yes button greys out when nobody can be reached
    Given a group whose only GSR has neither phone nor email
    When the volunteer opens the group to verify it
    Then Yes is disabled
    And ticking "standing in" without a name keeps it disabled
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

  Scenario: A release build carries no developer credentials
    Given an APK built with -p:UseDevCredentials=false
    Then devsettings.json is not among its embedded resources

  Scenario: The logs survive an uninstall
    Given a tablet that has registered a group
    When the app is uninstalled and reinstalled
    Then registrations.log is still in Documents
